from __future__ import annotations

from typing import Dict, List

from pydantic import BaseModel, Field

ISSUE_OPTIONS: Dict[str, List[str]] = {
    "tempo": ["slow", "normal", "fast"],
    "guidance": ["low", "medium", "high"],
    "complexity": ["low", "medium", "high"],
    "stimulus": ["low", "medium", "high"],
    "break_policy": ["rare", "on_demand", "frequent"],
    "feedback": ["summary", "brief_immediate", "detailed_immediate"],
    "taste": ["polite", "concise", "encouraging", "neutral"],
}

DEFAULT_ISSUE_SETTINGS: Dict[str, str] = {
    "tempo": "normal",
    "guidance": "medium",
    "complexity": "medium",
    "stimulus": "medium",
    "break_policy": "on_demand",
    "feedback": "brief_immediate",
    "taste": "polite",
}


class userData(BaseModel):
    name: str = "None"
    ex_status: str = "None"
    cl_condition: str = "None"
    low_threshold: float = 0
    high_threshold: float = 1000
    issue_settings: Dict[str, str] = Field(
        default_factory=lambda: DEFAULT_ISSUE_SETTINGS.copy()
    )


user_status: Dict[str, userData] = {
    "01": userData(),
    "02": userData(),
    "03": userData(),
}
