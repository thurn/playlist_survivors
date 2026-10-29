# TD7: the budget's GPU figures, checked against published sources

Checked on 2026-10-27 from Cygnus (web search and fetch). performance.md's GPU translation uses
Steel Nomad about 5,040 (RTX 4070 Ti) against 2,025 (RTX 4060 Laptop), and memory bandwidth
504 GB/s against 256 GB/s.

| Figure | performance.md | Published | Source | Verdict |
|---|---|---|---|---|
| Steel Nomad DX12, RTX 4070 Ti | ≈5,040 | 5,040 (median of user results) | [UL Benchmarks, RTX 4070 Ti](https://benchmarks.ul.com/hardware/gpu/NVIDIA+GeForce+RTX+4070+Ti+review) | confirmed |
| Steel Nomad DX12, RTX 4060 Laptop | ≈2,025 | 2,267 (median, the 115 W notebook entry) | [UL Benchmarks, NVIDIA RTX 4060 (notebook)](https://benchmarks.ul.com/hardware/gpu/NVIDIA+RTX+4060+(notebook)+review) | **not confirmed**: published medians are 11 to 12% higher |
| Steel Nomad, RTX 4060 Laptop | | median 2,236, min 1,829, max 2,351 (9 results) | [Notebookcheck, RTX 4060 Laptop GPU](https://www.notebookcheck.net/NVIDIA-GeForce-RTX-4060-Laptop-GPU-Benchmarks-and-Specs.675692.0.html) | agrees with UL, not with 2,025 |
| Memory bandwidth, RTX 4070 Ti | 504 GB/s | 504 GB/s (192-bit, 21 Gbps GDDR6X); L2 48 MB | [PCPer review](https://pcper.com/2023/01/nvidia-geforce-rtx-4070-ti-review-featuring-asus/), [TechSpot specs](https://www.techspot.com/products/graphics-cards/nvidia-geforce-rtx-4070-ti.258458/) | confirmed |
| Memory bandwidth, RTX 4060 Laptop | 256 GB/s | 256 GB/s (128-bit, 16 Gbps GDDR6) | [Notebookcheck, RTX 4060 Laptop GPU](https://www.notebookcheck.net/NVIDIA-GeForce-RTX-4060-Laptop-GPU-Benchmarks-and-Specs.675692.0.html) | confirmed |

Other figures seen, not used:

- The desktop RTX 4060's UL median is 2,040 ([UL Benchmarks, RTX 4060](https://benchmarks.ul.com/hardware/gpu/NVIDIA+GeForce+RTX+4060+review)),
  close to the design's 2,025, so the 2,025 may have come from the desktop card. That card is not
  the target.
- Notebookcheck's minimum of 1,829 shows the low-power spread the 1.2× margin covers.
- UL's RTX 4060 Laptop GPU page ("NVIDIA GeForce RTX 4060 Laptop GPU") was temporarily unavailable
  when checked; the notebook entry above is UL's listing for the same chip.

What it means for the pass lines:

- With 2,267, the compute ratio is 5,040 / 2,267 = **2.22×**, not 2.5×. The GPU line would be
  12.5 / 2.22 / 1.2 = **4.69 ms** instead of 4.2 ms, and the compute estimate ×2.67 instead of ×3.0.
- TD7 keeps performance.md's 4.2 ms and ×3.0 (the harness reads its lines from the sweep spec,
  `lines.gpuMs`, defaulting to 4.2): they are about 11% conservative against the published
  median, and the design, not a task's notes, should change a pass line. The bandwidth ratio
  (504 / 256 = 1.97×) stands as written.
