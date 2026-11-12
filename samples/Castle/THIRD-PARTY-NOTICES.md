# Castle sample third-party notices

The Castle sample is a RayBork recreation of raylib's `models_loading` example. Its sample-only
notices apply only to this directory. They are not part of RayBork's MIT license and are not listed
in the repository-level [THIRD-PARTY-NOTICES.md](../../THIRD-PARTY-NOTICES.md).

## raylib `models_loading` example

The sample reproduces the behavior of raylib's `models_loading` example: the window title, the
initial camera position and target, `DrawGrid(20, 10.0f)`, loading the castle model and its diffuse
texture, and the unlit default model shading.

- Upstream: <https://github.com/raysan5/raylib>
- Example source: <https://github.com/raysan5/raylib/blob/master/examples/models/models_loading.c>
- License: zlib/libpng, Copyright (c) 2014-2025 Ramon Santamaria (@raysan5)

The example is a behavior reference only; no raylib source code is copied or compiled here.

## Castle model and texture

- Files: `Castle.Assets/Assets/models/obj/castle.obj` and
  `Castle.Assets/Assets/models/obj/castle_diffuse.png`, copied verbatim from raylib's example
  resources.
- Author: Alberto Cano
- Pack: Medieval Assets Pack
- License: Creative Commons Attribution-NonCommercial 4.0 International (CC BY-NC 4.0)
- License text: <https://creativecommons.org/licenses/by-nc/4.0/legalcode>

**The castle model and texture are sample-only.** They are not part of RayBork, are not covered by
RayBork's MIT license, and may not be used commercially. They are included only so this sample can
reproduce the raylib example; do not copy them outside the sample. Replace them with your own assets
to remove this restriction.

RayBork's own license and dependency notices are the repository [LICENSE](../../LICENSE) and
[THIRD-PARTY-NOTICES.md](../../THIRD-PARTY-NOTICES.md).
