# SW5 cold review

Reviewer: fresh read-only same-host `/root/sw5_warden`, using the warden
skill, against base12db4a0a34ad38ed77672862799165510c28767b and the complete
working-tree implementation, tests and evidence. All ten original evolution
checks passed independently. One finding:

- P2: Soul Eater recovery growth was multiplied by Might. Source
  `VortexWeapon.PPower` adds that growth after the already Might-scaled base.
  At Spinach5 /600 recovered HP the implementation dealt4.5 instead of4;
  at the growth cap it dealt12 instead of9. Add a non-default-Might regression.

Confirmed by independently reading the private extraction's VortexWeapon
and base Weapon methods. The Vortex source SHA256 matches the already kept
SF1 audit:2b449ddd136d91a8bddc45a566748e61e4bc4e4b35815bc532b7df92b7ff7228.
No extracted source/data is copied into the repository. The calculation is
corrected to `base power × Might + recovery bonus`, and a Spinach5 regression
checks actual shot and contact damage at600 recovery and at the cap.
The original Might1 growth checks remain. No confirmation review is run.
