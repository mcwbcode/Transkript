-- ╔══════════════════════════════════════════════════════════════════════════╗
-- ║  Transkript — Supabase Schema                                           ║
-- ╠══════════════════════════════════════════════════════════════════════════╣
-- ║  Dashboard → SQL Editor → New query → colle ce fichier → Run           ║
-- ╚══════════════════════════════════════════════════════════════════════════╝


-- ══════════════════════════════════════════════════════════════════════════════
-- 1. TABLE : profiles
--    Étendue d'auth.users. Créée automatiquement à l'inscription.
-- ══════════════════════════════════════════════════════════════════════════════

CREATE TABLE IF NOT EXISTS public.profiles (
    id           UUID        PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,
    email        TEXT        NOT NULL,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_login   TIMESTAMPTZ,
    app_version  TEXT                                    -- version de l'app utilisée
);

-- Accès en lecture/écriture uniquement pour le propriétaire
ALTER TABLE public.profiles ENABLE ROW LEVEL SECURITY;

CREATE POLICY "profiles: lecture propre"
    ON public.profiles FOR SELECT
    USING (auth.uid() = id);

CREATE POLICY "profiles: mise à jour propre"
    ON public.profiles FOR UPDATE
    USING (auth.uid() = id);


-- ══════════════════════════════════════════════════════════════════════════════
-- 2. TABLE : licences
--    Gère le plan de chaque utilisateur (free / pro / beta).
--    Créée automatiquement avec le plan 'free' à l'inscription.
-- ══════════════════════════════════════════════════════════════════════════════

CREATE TABLE IF NOT EXISTS public.licences (
    id           BIGSERIAL    PRIMARY KEY,
    user_id      UUID         NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    plan         TEXT         NOT NULL DEFAULT 'free',   -- 'free' | 'beta' | 'pro'
    valid_until  TIMESTAMPTZ,                            -- NULL = illimité
    created_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    notes        TEXT                                    -- usage interne
);

ALTER TABLE public.licences ENABLE ROW LEVEL SECURITY;

CREATE POLICY "licences: lecture propre"
    ON public.licences FOR SELECT
    USING (auth.uid() = user_id);


-- ══════════════════════════════════════════════════════════════════════════════
-- 3. TRIGGERS : auto-création profil + licence à l'inscription
-- ══════════════════════════════════════════════════════════════════════════════

-- Fonction principale (appelée une seule fois, crée les deux lignes)
CREATE OR REPLACE FUNCTION public.handle_new_user()
RETURNS TRIGGER
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
BEGIN
    -- Profil
    INSERT INTO public.profiles (id, email)
    VALUES (NEW.id, NEW.email)
    ON CONFLICT (id) DO NOTHING;

    -- Licence free par défaut
    INSERT INTO public.licences (user_id, plan)
    VALUES (NEW.id, 'free')
    ON CONFLICT DO NOTHING;

    RETURN NEW;
END;
$$;

-- Supprime l'ancien trigger si il existait, puis recrée
DROP TRIGGER IF EXISTS on_auth_user_created ON auth.users;

CREATE TRIGGER on_auth_user_created
    AFTER INSERT ON auth.users
    FOR EACH ROW
    EXECUTE FUNCTION public.handle_new_user();


-- ══════════════════════════════════════════════════════════════════════════════
-- 4. FONCTION : vérifier si un utilisateur a accès
--    Utilisable depuis l'app via RPC (optionnel)
-- ══════════════════════════════════════════════════════════════════════════════

CREATE OR REPLACE FUNCTION public.has_access()
RETURNS BOOLEAN
LANGUAGE sql
SECURITY DEFINER
SET search_path = public
AS $$
    SELECT EXISTS (
        SELECT 1
        FROM public.licences
        WHERE user_id = auth.uid()
          AND plan IN ('free', 'beta', 'pro')
          AND (valid_until IS NULL OR valid_until > NOW())
    );
$$;


-- ══════════════════════════════════════════════════════════════════════════════
-- 5. VUE : résumé utilisateurs (admin uniquement — usage interne)
-- ══════════════════════════════════════════════════════════════════════════════

CREATE OR REPLACE VIEW public.v_users AS
SELECT
    p.id,
    p.email,
    p.created_at,
    p.last_login,
    p.app_version,
    l.plan,
    l.valid_until
FROM public.profiles p
LEFT JOIN public.licences l ON l.user_id = p.id;

-- Cette vue n'est PAS exposée via RLS → accessible uniquement via service_role
