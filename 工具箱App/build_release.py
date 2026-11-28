#!/usr/bin/env python3
"""生成发布到线上服务器的版本包。

产出：
  dist/version.json   —— { version, url, note }
  dist/toolbox-<v>.zip —— 完整的网页包（APK 更新时下载解压的就是它）

流程：
  改完 mobile/ 的代码 → 跑这个脚本 → 把 dist/ 里的东西传到托管平台 → 手机上点检查更新
"""
import json
import os
import shutil
import zipfile
import datetime
import re

HERE = os.path.dirname(os.path.abspath(__file__))
MOBILE = os.path.abspath(os.path.join(HERE, "mobile"))
DIST = os.path.abspath(os.path.join(HERE, "dist"))

VERSION = "1.0.0"
NOTE = "首个版本，13 个工具"

# 打包时忽略的文件
SKIP = {"make_icons.py", "make_android_icons.py"}
SKIP_EXT = {".py", ".pyc", ".zip", ".log"}


def read_version():
    """从 app.js 里读当前版本号，保持两边一致"""
    p = os.path.join(MOBILE, "js", "app.js")
    with open(p, "r", encoding="utf-8") as f:
        m = re.search(r"APP_VERSION\s*=\s*'([^']+)'", f.read())
    return m.group(1) if m else VERSION


def main():
    version = read_version()
    os.makedirs(DIST, exist_ok=True)

    zip_name = f"toolbox-{version}.zip"
    zip_path = os.path.join(DIST, zip_name)

    count = 0
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
        for root, dirs, files in os.walk(MOBILE):
            dirs[:] = [d for d in dirs if d not in ("__pycache__", ".git")]
            for name in files:
                if name in SKIP:
                    continue
                ext = os.path.splitext(name)[1].lower()
                if ext in SKIP_EXT:
                    continue
                full = os.path.join(root, name)
                rel = os.path.relpath(full, MOBILE).replace("\\", "/")
                z.write(full, rel)
                count += 1

    info = {
        "version": version,
        "url": f"toolbox-{version}.zip",
        "note": NOTE,
        "builtAt": datetime.datetime.now().isoformat(timespec="seconds"),
        "files": count,
    }
    with open(os.path.join(DIST, "version.json"), "w", encoding="utf-8") as f:
        json.dump(info, f, ensure_ascii=False, indent=2)

    # 顺便出一份纯网页版（给「添加到主屏幕」那条路用）
    site = os.path.join(DIST, "site")
    if os.path.exists(site):
        shutil.rmtree(site)
    shutil.copytree(MOBILE, site, ignore=shutil.ignore_patterns("*.py", "*.pyc", "__pycache__"))

    print(f"版本 {version}，{count} 个文件")
    print("产出：")
    print("  ", zip_path)
    print("  ", os.path.join(DIST, "version.json"))
    print("  ", site)


if __name__ == "__main__":
    main()
