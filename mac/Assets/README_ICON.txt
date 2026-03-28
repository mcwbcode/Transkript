Place your Transkript.icns file here.

To generate Transkript.icns from a 1024x1024 PNG:

1. Create an iconset folder:
   mkdir Transkript.iconset

2. Generate all required sizes (requires sips on macOS):
   sips -z 16 16     Transkript.png --out Transkript.iconset/icon_16x16.png
   sips -z 32 32     Transkript.png --out Transkript.iconset/icon_16x16@2x.png
   sips -z 32 32     Transkript.png --out Transkript.iconset/icon_32x32.png
   sips -z 64 64     Transkript.png --out Transkript.iconset/icon_32x32@2x.png
   sips -z 128 128   Transkript.png --out Transkript.iconset/icon_128x128.png
   sips -z 256 256   Transkript.png --out Transkript.iconset/icon_128x128@2x.png
   sips -z 256 256   Transkript.png --out Transkript.iconset/icon_256x256.png
   sips -z 512 512   Transkript.png --out Transkript.iconset/icon_256x256@2x.png
   sips -z 512 512   Transkript.png --out Transkript.iconset/icon_512x512.png
   sips -z 1024 1024 Transkript.png --out Transkript.iconset/icon_512x512@2x.png

3. Convert to .icns:
   iconutil -c icns Transkript.iconset

4. Move the result here:
   mv Transkript.icns mac/Assets/Transkript.icns
