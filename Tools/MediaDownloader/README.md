# Media Downloader

クリップボードからHTTP/HTTPS URLを抽出し、yt-dlpで動画をダウンロードするWindows用GUIツールです。Pythonの標準ライブラリだけで動作します。

## 必要条件

- Python 3.9以降（Tkinterを含む通常のWindows版）
- `yt-dlp` がPATH上で実行可能であること
- サムネイルを埋め込むための `ffmpeg`

## 使い方

1. ダウンロードする動画のURLをコピーします。
2. `MediaDownloader.cmd` または `MediaDownloader.py` を実行します。
3. **クリップボードからダウンロード** を押します。

動画はユーザーの `Downloads` フォルダーに保存されます。yt-dlp は `--embed-metadata --embed-thumbnail` を指定して実行されます。
