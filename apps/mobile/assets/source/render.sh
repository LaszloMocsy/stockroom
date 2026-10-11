#!/bin/sh
# Renders the app's icon and splash images from the SVG sources here into apps/mobile/assets.
# Needs ImageMagick 7 (`magick`). Run it after editing an SVG, and commit both.
set -eu
cd "$(dirname "$0")"

# iOS has no transparency in app icons.
magick -background "#0a5cc2" icon.svg -alpha remove -alpha off -strip ../icon.png
for name in android-icon-foreground android-icon-monochrome splash-icon; do
  magick -background none "$name.svg" -strip "../$name.png"
done
