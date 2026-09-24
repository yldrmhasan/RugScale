# B163A real-raster RugScale fixture

This directory carries the exact 8-bit indexed source bitmap supplied for the B163A RugScale audit.

- Original file: `CERP-MD2_B163A_PD277_0016_org.bmp`
- Source raster: `960 x 1500`
- Physical source size represented by this raster: `200 x 300`
- Source quality used by RugScale audit: `48 x 50`
- 160 x 230 target raster at the same quality: `768 x 1150`
- SHA-256 of reconstructed BMP: `9437ea0e9d9615ffd46037516610d53d7fdf37a96835d90aee9fbf90522918da`
- Used palette indices: `0, 1, 2, 3, 5, 6, 7, 8`

The binary BMP is losslessly XZ-compressed and then base64-split into
`CERP-MD2_B163A_PD277_0016_org.bmp.xz.b64.partXX` files. GitHub Actions concatenates the
parts, decodes them, and verifies the original SHA before running any test.

The audit intentionally does not treat Nearest Neighbor as ground truth. Every source catalogue
motif is projected to the target and measured for shape/alignment, indexed-color agreement and
role retention. Nearest is only a diagnostic baseline. A smaller deterministic subset also runs
the interactive source-motif detector to measure whether the expected original-source location
appears first / inside the candidate list.
