# In-game check for the example patches

The unit tests prove the PBOs are well-formed and the edited models re-parse. They do not prove the engine accepts the edits. This check does.

## Patch a copy of the dev dependencies

1. Back up the three PBOs in `B:/Steam/steamapps/common/Arma 3/uksf-dev/@uksf_dependencies/addons/`: `Ballistic_missiles.pbo`, `RHICC.pbo`, `rksla3-pumahc.pbo`. The tool replaces the directory entry, so hard-linked copies elsewhere stay pristine. The backup is for your own restore.
2. Run, with the state folder outside the mod:
   ```
   uksf-patcher apply --addons "B:/Steam/steamapps/common/Arma 3/uksf-dev/@uksf_dependencies/addons" --patches D:/Arma/uksf-patcher/examples/patches --state "C:/Users/Tim/.agent-scratch/patcher-state"
   ```
   Expected output: three `patched (first patch)` lines. Expected sha256 values, from the specimen run:
   - `Ballistic_missiles.pbo` -> `822e27bdd98dbe0456ad089be5accf1365ca010b8da33bc82396d575aab5f1ab`
   - `RHICC.pbo` -> `f94428027e41b0ee2e8c81bcb442a058b65e129a95bdef4d62192a3637ee7c08`
   - `rksla3-pumahc.pbo` -> `d88adb644e4bab92d08efed4ad2b0eea22ed3acff0cde054d90d03db4bcf136d`
3. The edited PBOs are newer than their `.bisign` files. Run the client with signature checks off, or re-sign them with the dev key.

## Spawn and read the RPT

Spawn `RHICC_GREEN`, `rksla3_puma_hc1_troop`, `rksla3_puma_hc1_1gpmg`, `rksla3_puma_hc1_2gpmg`, `v2_launchpad1`, `v2`, `shahab3`, `iskander_9K720`, and `iskander_launchpad1`.

These lines must be gone:

| PBO | RPT line |
|---|---|
| RHICC.pbo | `RHICC: Invalid parent bone 'swing_2' for 'otocvez_2'` |
| RHICC.pbo | `RHICC: Invalid parent bone 'swing_3' for 'otocvez_3'` |
| rksla3-pumahc.pbo | `Strange convex component135` ... `component149 in rksla3\rksla3-pumahc\rksla3_puma_hc1_troop.p3d:geometryFire` (15 lines) |
| rksla3-pumahc.pbo | `Strange convex component51 in rksla3\rksla3-pumahc\rksla3_puma_hc1_troop.p3d:geometryView` |
| Ballistic_missiles.pbo | `Warning: ballistic_missiles\objects\<model>.p3d:<n> Face <n>, point <n>, face points <a>,<b>,<c> - very small normal 0,0,0` for v2_launchpad1, v2, shahab3, iskander_9K720, iskander_launchpad1 |
| Ballistic_missiles.pbo | `Warnings in ballistic_missiles\objects\<model>.p3d:<n>` for the same models |

No new line may name these three PBOs or their models (signature, `Cannot load`, `LZO`, `ODOL`, `selection`, `bone`, `skeleton`).

## Behaviour to confirm

- RHICC: turrets 2 and 3 (the M240 mounts on `otocvez_2` and `otocvez_3`) still traverse with a player in the seat, as they did before. The bones were already root bones in practice; the patch only makes the empty parent explicit.
- Puma: the crew and cargo still render in their seats. Rounds fired at a seated crew member still hit them; the proxy triangles stay in the fire and view geometry, only their component names went.
- Missiles: each model looks the same as before. Only zero-area faces were removed (143, 16, 91, 237 and 9 faces).

## Restore

Delete the manifest files from a copy of the patches folder and run `apply` with that folder and the same `--state`. The tool restores each PBO from its pristine backup. Or copy your backups from step 1 back.
