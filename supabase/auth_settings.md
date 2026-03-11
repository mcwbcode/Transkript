# Configuration Supabase — Transkript

## 1. Exécuter le schema SQL
Dashboard → **SQL Editor** → New query → colle `schema.sql` → **Run**

---

## 2. Authentication → Providers
- **Email** : activé ✅
- **Confirm email** : à toi de choisir :
  - `OFF` → connexion immédiate après inscription (recommandé pour beta)
  - `ON` → email de confirmation envoyé (plus sécurisé pour prod)

---

## 3. Authentication → URL Configuration
| Champ | Valeur |
|-------|--------|
| Site URL | `https://mcwbcode.github.io/Transkript-V1-` |
| Redirect URLs | `https://mcwbcode.github.io/Transkript-V1-/register.html` |

---

## 4. Authentication → Email Templates (optionnel)
Personnalise l'email de confirmation avec le nom **Transkript**.

---

## 5. JWT Expiry
Dashboard → **Authentication → Settings → JWT expiry**
- Valeur recommandée : `3600` (1 heure) — l'app rafraîchit automatiquement le token.

---

## 6. Vérifier que tout fonctionne
Après avoir exécuté le SQL, crée un compte test depuis `register.html`.
Vérifie dans **Table Editor → profiles** et **Table Editor → licences**
que les lignes ont bien été créées automatiquement.
