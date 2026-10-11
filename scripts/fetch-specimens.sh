#!/usr/bin/env bash
set -euo pipefail

addons="${1:-B:/Steam/steamapps/common/Arma 3/uksf-dev/@uksf_dependencies/addons}"
rtm="${2:-D:/Arma/uksf_air/addons/apache/data/Anim/AH64_pilot.rtm}"
root="$(cd "$(dirname "$0")/.." && pwd)"

mkdir -p "$root/testdata/specimens"
for pbo in Ballistic_missiles.pbo RHICC.pbo rksla3-pumahc.pbo rnc_apartments.pbo sps_ai_axmc_ace.pbo uk3cb_baf_weapons_L110.pbo ew_modular.pbo; do
    cp "$addons/$pbo" "$root/testdata/specimens/$pbo"
done
cp "$rtm" "$root/testdata/AH64_pilot.rtm"
