# uksf-patcher

Patches Arma 3 dependency PBOs in place from declarative, hash-pinned manifests. Nobody edits a dependency PBO by hand: the manifests are the source of truth, and the modpack build runs this CLI before it signs the dependencies.

## Usage

```
uksf-patcher apply  --addons <dir> --patches <dir> --state <dir> [--dry-run]
uksf-patcher verify --addons <dir> --patches <dir> --state <dir>
uksf-patcher inspect <pbo> [filter]           list entries (packing, sizes, names)
uksf-patcher hash <pbo> [path-in-pbo]         sha256 of the whole PBO, or of one asset, for pinning
uksf-patcher p3d <pbo> <path-in-pbo> [lod]    ODOL summary: skeleton, LODs, sections, selections
```

- `--addons`: the `@mod/addons` folder that holds the PBOs.
- `--patches`: a folder of manifests, one `*.json` per target PBO. The modpack keeps one folder per repo mod, named for it: `patches/@uksf_dependencies`, `patches/@CUP_Vehicles`. The build runs the tool once for each mod folder, with state in `PatcherState/@<mod>`, and signs every patched PBO with the dependencies key.
- `--state`: where the tool keeps per-PBO state and a pristine backup of every PBO it patched. It must be outside the mod folder, or the backups would ship to clients. The build keeps it between builds.
- Exit code 0 on success. Exit code 1 on any error (with a message on stderr), or when `verify` finds a PBO that is not patched.

`apply`, for each manifest:

| Current PBO | Action |
|---|---|
| equals the recorded patched sha256, patch set unchanged | skip (`up to date`); the file and its timestamp stay untouched |
| equals the recorded or pending patched sha256, patch set changed | re-patch from the pristine backup |
| anything else (first run, pristine, or an upstream update) | treat the current file as pristine and patch it, if it matches `pristineSha256` |

The pristine PBO (the current file, or the backup when the current file is the tool's own output) must match the manifest's `pristineSha256` and carry a valid SHA-1 trailer. Every asset's pinned sha256 is checked before any op runs. Any mismatch stops the run with the PBO, the file or asset, and the expected and actual hashes, and writes nothing. An upstream update therefore fails until the manifest is reviewed and re-pinned. `apply --dry-run` and `verify` run the same checks.

The patch-set hash covers the manifest bytes, every file a `replace-file` op names, and the tool's output revision. The tool writes the backup, then a state file that keeps the installed output as committed and the new output as pending, then the patched PBO through a temporary file and a move, then the state with the pending output committed. A run interrupted at any point is finished by the next `apply`: the current PBO is recognised as either the committed or the pending output, and the patch is rebuilt from the backup. The move replaces the directory entry, so a hard-linked original elsewhere is not changed.

When a manifest is removed, the next `apply` restores the pristine PBO from the backup and deletes the state and backup. When the current PBO is neither pristine nor the tool's output, `apply` forgets the state and keeps the backup in `--state`. The backup is deleted only when the PBO on disk is the pristine bytes.

`verify` re-applies every manifest to its pristine backup and checks that the result equals the PBO on disk.

Output PBOs are byte-identical to the input apart from the edited entries and the trailer SHA1. Unedited entry blocks, the header bytes, mangled (obfuscated) names and timestamps are copied verbatim. Edited entries are written uncompressed (packing 0, original size = data size).

## Manifest

```json
{
  "format": 1,
  "pbo": "RHICC.pbo",
  "pristineSha256": "<sha256 of the whole pristine PBO; uksf-patcher hash <pbo>>",
  "description": "free text, ignored",
  "assets": [
    {
      "path": "rhicc\\addons\\rhicc.p3d",
      "sha256": "<sha256 of the pristine, decompressed asset; uksf-patcher hash>",
      "ops": [
        { "op": "set-bone-parent", "bone": "otocvez_2", "from": "swing_2", "to": "" }
      ]
    }
  ]
}
```

`pristineSha256` is required. `path` matches case-insensitively, with or without the PBO prefix. Ops run in order on the asset bytes. JSON comments are allowed.

To add a new entry, set `copyOf` to an existing asset. `sha256` then pins that source asset, the ops run on a copy of it, and the result is added to the PBO at `path`. The source stays unchanged. The run fails if `path` already exists. Point config at the new path from a UKSF addon.

## Ops

| Op | Fields | Asset |
|---|---|---|
| `replace-file` | `source`: file next to the manifest | any |
| `text-replace` | `find`, `replace`, `count` (default 1; exact number of matches required) | text (refuses rapified configs) |
| `rtm-strip-bones` | `bones`: names to remove from the bone table and every frame | `RTM_0101` |
| `set-bone-parent` | `bone`, `from` (current parent, checked), `to` (`""` or an existing bone; cycles rejected) | ODOL v73/74 |
| `set-skeleton-name` | `from` (current name, checked), `to` (non-empty) | ODOL v73/74 |
| `rebind-vertices` | `lod`, `section`, `from`, `to` (bones), `box` (x/y/z min-max), `vertices` (exact count) | ODOL v73/74 |
| `add-translation` | `animation`, `bone`, `animationSource`, `minValue`, `maxValue`, `offset0`, `offset1`, `axis`, `address` (default clamp) | ODOL v73/74 |
| `strip-animations` | `sources`: the exact set of animation sources the model uses (checked) | ODOL v73-75 |
| `remove-component` | `lod`, `components`, `requireProxyFaces` (default true: every face of the component must be a proxy face) | ODOL v73/74 |
| `convex-hull-component` | `lods`, `component` | ODOL v73/74 |
| `remove-degenerate-faces` | `lods` (default all), `minArea` (default 1e-8 m²), `expectedRemoved` | MLOD |
| `stretch-along-axis` | `lods`, `axis` (origin and direction), `cut`, `length`, `vertices` (exact count), `bones` (optional; vertices bound only to these, and their proxies), `maxRadius` (optional) | ODOL v73-75 |
| `remove-faces` | `lod`, `axis`, optional `bones` (omit to match every vertex), `allBeyond`, `anyBeyond` (distances along the axis), optional `wholeParts` (test each connected part as a whole, so one part can be cut from another that overlaps it along the axis), `faces` (exact count) | ODOL v73-75 |
| `add-mesh` | `lods`, `source` (MLOD next to the manifest), `sourceAxis`, `axis`, `attachAlong`, `scale`, `weldLoops` (source z and radius of each open loop; each point snaps to the nearest barrel vertex within 6 mm), `loopRadii` (optional `[z, sourceRadius, targetRadius]`), `bone`, `uvFrom` (the cylinder band whose UV unwrap the new faces continue; an optional `fit` band with its own `lod` takes the unwrap from a cleaner LOD, for a decimated low LOD whose barrel is no longer a cylinder) | ODOL v73-75 |
| `restore-lod-proxies` | `from` (the LOD that has every proxy, such as `1`), `lods`, `filter` (proxy model substring). Adds each matching proxy that a target LOD lacks, with the source's model, sequence, transform and bone, onto the LOD's existing `proxy:` selection. Its section is that of a surviving proxy of the same model, or the first section with the source section's texture. Refuses a target LOD that lacks none | ODOL v73-75 |
| `rebuild-shadow-volume` | `lod` (the shadow volume, such as `10000`), `from` (the closed triangulated shadow LOD it is built from, such as `11000`). Rebuilds the volume as each source triangle plus a degenerate quad on each shared edge, with per-vertex neighbour data. Run it after every edit to the source LOD | ODOL v73-75 |

LOD names: `geometry`, `geometryFire`, `geometryView`, `geometryPhys`, `memory`, `landContact`, `hitpoints`, ..., or a visual resolution such as `0`.

ODOL and MLOD ops refuse a model unless the UKSFTA-BIS library re-serialises it byte-identically, and re-parse their own output. ODOL ops read the version header first and refuse anything outside 73-75, and a v75 model with nonzero encryption markers, before the library parses the model. An asset the library cannot read is reported as an error that names the PBO and the asset. `convex-hull-component` also checks that every LOD's sections still partition its faces in order.

- `strip-animations` replaces the model's animation block with an empty one and moves the LOD addresses that follow it. It edits the bytes in place and does not parse the rest of the model, so it also works on unencrypted v75 models that the library cannot read. It is for an inventory item that reuses a vehicle model whose animations need sources the item does not have.
- `remove-component` deletes the component's named selection and remaps the LOD's proxy selection indices. It is for proxy triangles that were named as components, which Arma reports as `Strange convex component`.
- `convex-hull-component` replaces the component's faces with the triangles of its convex hull. It keeps every vertex and the selection name, adds the hull faces to every selection that held all of the old faces, and recomputes the section face ranges. The winding follows the LOD's other components. It refuses flat components, and a selection that holds only part of the component. The hull changes the collision shape, so check the result: a hull of an open L-shaped wall fills the corner.
- `remove-degenerate-faces` drops faces whose area is below `minArea` from every face-indexed tagg (selections, `#Selected#`, `#Lock#`, `#UVSet#`). Arma logs these faces as `very small normal 0,0,0` when it binarises the model at load.

## Build and test

```
scripts/fetch-specimens.sh        # copies the test PBOs into testdata/ (git-ignored)
dotnet test
dotnet build src/UksfPatcher -c Release
```

The project references `D:/Arma/_ref/UKSFTA-BIS` (BIS.Core, BIS.P3D, BIS.RTM) through relative project paths. The library's ODOL reader prints debug lines to stderr; the tool mutes stderr while it parses.

Example manifests for real defects are in `examples/patches`. See `docs/ingame-check.md` for the in-game check.
