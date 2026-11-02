"""Edge cuts (design.md, The Look > Edge cuts; plan.md, Rendering > Edge cuts): each model's committed recipe,
rhythm_champions/tools/recipes/edgecut/<model>.py, redraws a vendor model as a few chosen lines over a dark low-poly
body. A recipe names its vendor sources (`MODEL`) and builds the cut from them (`cut(source) -> Cut`); `champions
edgecut <model>` runs it between Unity's export of the vendor model and the bake into the crowd's assets."""

from __future__ import annotations

from dataclasses import asdict, dataclass, field

from .cut import Cut
from .source import Selection, Source

__all__ = ["Clip", "Cut", "Model", "Selection", "Source"]


@dataclass(frozen=True)
class Clip:
    """A clip the crowd plays: its role (idle, walk, attack, hit, ...), the vendor file's GUID and the clip's name,
    and its annotations in seconds and metres (negative where absent). Fields are named as model.json's."""

    role: str
    guid: str
    clip: str
    loop: bool = False
    impact: float = -1
    end: float = -1
    strideM: float = -1


@dataclass(frozen=True)
class Model:
    """A recipe's vendor sources: the prefab by GUID, its height at scale 1, a flyer's hover, children never shown,
    and the clips (manifest.json lists every file)."""

    id: str
    prefab: str
    heightM: float
    clips: tuple[Clip, ...]
    hoverM: float = 0.0
    hide: tuple[str, ...] = field(default_factory=tuple)

    def json(self) -> dict:
        d = asdict(self)
        d["clips"] = [asdict(c) for c in self.clips]
        d["hide"] = list(self.hide)
        return d
