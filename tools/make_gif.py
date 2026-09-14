"""캡처한 PNG 프레임 폴더를 GIF로 만든다.

사용: python tools/make_gif.py artifacts/frames/gate docs/images/gate-demo.gif --fps 12 --width 960
"""
import argparse
import pathlib

from PIL import Image


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("frames", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    parser.add_argument("--fps", type=float, default=12)
    parser.add_argument("--width", type=int, default=960)
    parser.add_argument("--colors", type=int, default=128)
    parser.add_argument("--hold-last", type=float, default=1.5, help="마지막 프레임 정지 시간(초)")
    args = parser.parse_args()

    paths = sorted(args.frames.glob("*.png"))
    if not paths:
        raise SystemExit(f"프레임 없음: {args.frames}")

    frames = []
    for path in paths:
        image = Image.open(path).convert("RGB")
        if image.width != args.width:
            height = round(image.height * args.width / image.width)
            image = image.resize((args.width, height), Image.LANCZOS)
        frames.append(image)

    # 첫 프레임 팔레트를 공유하면 프레임 간 색이 튀지 않는다.
    palette = frames[0].quantize(colors=args.colors, method=Image.Quantize.MEDIANCUT)
    quantized = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in frames]

    frame_ms = round(1000 / args.fps)
    durations = [frame_ms] * len(quantized)
    durations[-1] = round(args.hold_last * 1000)

    args.output.parent.mkdir(parents=True, exist_ok=True)
    quantized[0].save(
        args.output,
        save_all=True,
        append_images=quantized[1:],
        duration=durations,
        loop=0,
        optimize=True,
        disposal=1,
    )
    size_kb = args.output.stat().st_size / 1024
    print(f"{args.output}: {len(quantized)} frames, {size_kb:.0f} KB")


if __name__ == "__main__":
    main()
