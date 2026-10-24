#!/usr/bin/env bash
set -euo pipefail

addons="${1:-B:/Steam/steamapps/common/Arma 3/uksf-dev/@uksf_dependencies/addons}"
rtm="${2:-D:/Arma/uksf_air/addons/apache/data/Anim/AH64_pilot.rtm}"
workshop="${3:-B:/Steam/steamapps/workshop/content/107410}"
arma="${4:-B:/Steam/steamapps/common/Arma 3}"
modpack="${5:-D:/Arma/modpack}"
root="$(cd "$(dirname "$0")/.." && pwd)"

mkdir -p "$root/testdata/specimens"
for pbo in Ballistic_missiles.pbo RHICC.pbo rksla3-pumahc.pbo rnc_apartments.pbo sps_ai_axmc_ace.pbo uk3cb_baf_weapons_L110.pbo ew_modular.pbo tbd_m119_m119.pbo tbd_mortars_105mm.pbo; do
    cp "$addons/$pbo" "$root/testdata/specimens/$pbo"
done
cp "$rtm" "$root/testdata/AH64_pilot.rtm"
cp "$modpack/patches/L118_muzzle.p3d" "$root/testdata/L118_muzzle.p3d"
cp "$workshop/1337653467/addons/EAWS_EF2000.pbo" "$root/testdata/specimens/EAWS_EF2000.pbo"
cp "$arma/Jets/Addons/air_f_jets.pbo" "$root/testdata/specimens/air_f_jets.pbo"
rm -rf "$root/testdata/typhoon" && mkdir -p "$root/testdata/typhoon"
cp -r "$modpack/patches/EAWS_EF2000.pbo.json" "$modpack/patches/EAWS_EF2000" "$root/testdata/typhoon/"
