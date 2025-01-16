from dataclasses import dataclass


@dataclass
class PackageReference:
    provider: str
    configuration: dict[str, str]