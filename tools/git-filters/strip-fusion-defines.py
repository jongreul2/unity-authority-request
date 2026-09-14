"""git clean 필터: ProjectSettings.asset에서 Fusion SDK가 로컬에 추가한 정의(FUSION2, FUSION_WEAVER)를 뺀다.

Fusion SDK는 라이선스 때문에 저장소에 넣지 않는다. SDK를 로컬에 가져오면 Fusion 설치기가
PlayerSettings에 정의를 추가하는데, 그대로 커밋하면 SDK 없는 CI에서 Fusion 어셈블리가
컴파일 대상이 되어 실패한다. 이 필터는 작업 사본은 두고 커밋 내용에서만 정의를 뺀다.

설정(저장소 루트에서 한 번):
    git config filter.strip-fusion.clean "python tools/git-filters/strip-fusion-defines.py"
    git config filter.strip-fusion.smudge cat
"""
import re
import sys

FUSION_DEFINES = {"FUSION2", "FUSION_WEAVER"}


def strip(text: str) -> str:
    lines = text.split("\n")
    out = []
    i = 0
    while i < len(lines):
        line = lines[i]
        if line.rstrip() == "  scriptingDefineSymbols:":
            entries = []
            i += 1
            while i < len(lines) and re.match(r"^    \S", lines[i]):
                key, _, value = lines[i].strip().partition(":")
                kept = [d for d in value.strip().split(";") if d and d not in FUSION_DEFINES]
                if kept:
                    entries.append(f"    {key}: {';'.join(kept)}")
                i += 1
            if entries:
                out.append(line)
                out.extend(entries)
            else:
                out.append("  scriptingDefineSymbols: {}")
            continue
        out.append(line)
        i += 1
    return "\n".join(out)


if __name__ == "__main__":
    data = sys.stdin.buffer.read().decode("utf-8")
    sys.stdout.buffer.write(strip(data).encode("utf-8"))
