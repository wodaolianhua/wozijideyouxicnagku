#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""百炼 API 调用小工具（仅用 Python 标准库，无需 pip 安装任何东西）
用来在 WorkBuddy 之外直接调百炼模型，并把结果写进文档。

用法：
  # 1) 设 API Key（任选一种，只需做一次）
  #    Windows cmd:      set DASHSCOPE_API_KEY=sk-xxxx
  #    PowerShell:       $env:DASHSCOPE_API_KEY="sk-xxxx"
  #    Git Bash/Linux:   export DASHSCOPE_API_KEY=sk-xxxx
  #    或：把同目录 .env.example 复制为 .env 并填入 key

  # 2) 单次调用（math 模型算数值，结果追加写入文档）
  python call_bailian.py --model qwen-math-turbo --prompt "你的问题" --out result.txt
  python call_bailian.py --model qwen-math-turbo --prompt-file mod1.txt --out mod1.txt

  # 3) 传图给视觉模型（美术质检）
  python call_bailian.py --model qwen3-vl-235b-a22b-thinking --prompt "挑毛病" --image shot.png --out review.txt

  # 4) 批量：把 prompt_dir 下所有 .txt 逐个调用，结果各自落盘到 out_dir
  python call_bailian.py --batch --prompt-dir prompts/ --out-dir results/ --model qwen-math-turbo
"""
import os, sys, json, base64, argparse, urllib.request, urllib.error

API_URL = "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions"


def get_key():
    key = os.environ.get("DASHSCOPE_API_KEY")
    if not key:
        ep = os.path.join(os.path.dirname(os.path.abspath(__file__)), ".env")
        if os.path.exists(ep):
            for line in open(ep, encoding="utf-8"):
                line = line.strip()
                if line and not line.startswith("#") and "=" in line:
                    k, v = line.split("=", 1)
                    if k.strip() == "DASHSCOPE_API_KEY":
                        key = v.strip().strip('"').strip("'")
    if not key:
        sys.exit("错误：未找到 API Key。\n"
                 "请设置环境变量 DASHSCOPE_API_KEY，\n"
                 "或在同目录 .env 写入：DASHSCOPE_API_KEY=sk-xxxx")
    return key


def build_messages(prompt, image_path):
    if image_path:
        with open(image_path, "rb") as f:
            b64 = base64.b64encode(f.read()).decode("utf-8")
        ext = os.path.splitext(image_path)[1].lower().lstrip(".")
        mime = {"png": "image/png", "jpg": "image/jpeg", "jpeg": "image/jpeg",
                "gif": "image/gif", "webp": "image/webp"}.get(ext, "image/png")
        return [{"role": "user", "content": [
            {"type": "image_url", "image_url": {"url": f"data:{mime};base64,{b64}"}},
            {"type": "text", "text": prompt}
        ]}]
    return [{"role": "user", "content": prompt}]


def call(model, messages, key, max_tokens):
    data = json.dumps({"model": model, "messages": messages,
                       "max_tokens": max_tokens}).encode("utf-8")
    req = urllib.request.Request(
        API_URL, data=data,
        headers={"Authorization": f"Bearer {key}", "Content-Type": "application/json"},
        method="POST")
    try:
        with urllib.request.urlopen(req, timeout=180) as r:
            resp = json.loads(r.read().decode("utf-8"))
        return resp["choices"][0]["message"]["content"]
    except urllib.error.HTTPError as e:
        return f"[HTTP错误 {e.code}] {e.read().decode('utf-8', 'ignore')}"
    except Exception as e:
        return f"[调用失败] {e}"


def main():
    ap = argparse.ArgumentParser(description="百炼 API 调用小工具")
    ap.add_argument("--model", default="qwen-math-turbo", help="模型名")
    ap.add_argument("--prompt", help="直接给提示词")
    ap.add_argument("--prompt-file", help="从文件读提示词")
    ap.add_argument("--image", help="传给视觉模型的图片路径")
    ap.add_argument("--out", help="结果写入文件（追加）")
    ap.add_argument("--max-tokens", type=int, default=4096)
    ap.add_argument("--batch", action="store_true", help="批量模式")
    ap.add_argument("--prompt-dir", help="批量：提示词目录")
    ap.add_argument("--out-dir", help="批量：结果输出目录")
    args = ap.parse_args()
    key = get_key()

    if args.batch:
        if not args.prompt_dir or not args.out_dir:
            sys.exit("--batch 需要 --prompt-dir 和 --out-dir")
        os.makedirs(args.out_dir, exist_ok=True)
        for fn in sorted(os.listdir(args.prompt_dir)):
            if not fn.endswith(".txt"):
                continue
            prompt = open(os.path.join(args.prompt_dir, fn), encoding="utf-8").read()
            print(f"调用 {args.model} <- {fn} ...")
            res = call(args.model, build_messages(prompt, args.image), key, args.max_tokens)
            outp = os.path.join(args.out_dir, fn)
            with open(outp, "w", encoding="utf-8") as f:
                f.write(res)
            print(f"  -> 已写入 {outp}")
        return

    if args.prompt_file:
        prompt = open(args.prompt_file, encoding="utf-8").read()
    elif args.prompt:
        prompt = args.prompt
    else:
        sys.exit("错误：需要 --prompt 或 --prompt-file")

    res = call(args.model, build_messages(prompt, args.image), key, args.max_tokens)
    if args.out:
        with open(args.out, "a", encoding="utf-8") as f:
            f.write(f"\n# 模型 {args.model}\n{res}\n")
        print(f"结果已追加写入: {args.out}")
    else:
        print(res)


if __name__ == "__main__":
    main()
