"""Edge cuts (design.md, The Look > Edge cuts; plan.md, Rendering > Edge cuts): each model's committed recipe,
rhythm_champions/tools/recipes/edgecut/<model>.py, redraws a vendor model as a few chosen lines over a dark low-poly
body. A recipe names its vendor sources (`MODEL`) and builds the cut from them (`cut(source) -> Cut`); `champions
edgecut <model>` runs it between Unity's export of the vendor model and the bake into the crowd's assets."""

from __future__ import annotations

from dataclasses import asdict, dataclass, field

from .cut import Cut
from .source import Selection, Source

__all__ = ["Clip", "Cut", "Model", "Selection", "Source", "Weapon"]


@dataclass(frozen=True)
class Clip:
    """A clip the crowd plays: its role (idle, walk, attack, hit, ...), the vendor file's GUID and the clip's name,
    its annotations in seconds and metres (negative where absent), and `liftM`, metres the whole clip is raised (a
    flyer's clips authored on the ground but played in the air). Fields are named as model.json's."""

    role: str
    guid: str
    clip: str
    loop: bool = False
    impact: float = -1
    end: float = -1
    strideM: float = -1
    liftM: float = 0.0


@dataclass(frozen=True)
class Weapon:
    """A weapon prefab (by GUID) held in a humanoid's right or left fist, its handle through the fist across the
    knuckles as Synty's combat clips hold one, then moved by its grip: `palm` metres (at the prefab's own scale) from
    the knuckles toward the palm, turned by `pitch`, `yaw` and `roll` degrees about its own axes."""

    prefab: str
    hand: str = "right"
    palm: float = 0.0
    pitch: float = 0.0
    yaw: float = 0.0
    roll: float = 0.0


@dataclass(frozen=True)
class Model:
    """A recipe's vendor sources: the prefab by GUID, its height at scale 1, a flyer's hover, children never shown,
    weapons put in its hands, the clips (manifest.json lists every file), the role of the clip whose first frame the
    line choice sees the model in (`pose`: a T-posed rig standing as in the game; empty, the bind pose), and the clip
    roles its review draws (`reviewPoses`: a boss's every attack). A model without clips is a structure: static, its
    height measured from the ground (its prefab's origin) up."""

    id: str
    prefab: str
    heightM: float
    clips: tuple[Clip, ...] = ()
    hoverM: float = 0.0
    hide: tuple[str, ...] = field(default_factory=tuple)
    weapons: tuple[Weapon, ...] = field(default_factory=tuple)
    pose: str = ""
    reviewPoses: tuple[str, ...] = ("idle", "walk", "attack")

    def json(self) -> dict:
        d = asdict(self)
        d["clips"] = [asdict(c) for c in self.clips]
        d["hide"] = list(self.hide)
        d["weapons"] = [asdict(w) for w in self.weapons]
        d["reviewPoses"] = list(self.reviewPoses)
        return d

    @property
    def static(self) -> bool:
        return not self.clips
