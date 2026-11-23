---
uid: neural-training-CHANGELOG
---
# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [1.0.0-exp.1] - 2026-11-19

### Added
- Neural texture compression as four block-compressed (BC1) latent textures and a small per-material network, decoded at shading time by the `StandardLit_NTC` Shader Graph (URP); a **Neural Texture Compression** block in URP's Graphics settings switches the decode on/off project-wide

### Known limitations
- Unity 6000.7 or newer, Universal Render Pipeline only;
- Making changes to NTC materials, such as changing the shader, or editing the material encoded list in Graphics Settings, will cause material to render black until you press playmode.
- textures in the stack must be power-of-two; a source material's texture tiling/offset is not applied to the trained result
- NTC is not compatible with URP specular workflow or alpha cutout or transparent materials.
