import queue
import re
import shutil
import subprocess
import threading
from pathlib import Path
from tkinter import DISABLED, NORMAL, Button, Label, TclError, Tk, messagebox
from tkinter.scrolledtext import ScrolledText
from urllib.parse import urlparse


URL_PATTERN = re.compile(r'https?://[^\s<>"\']+')
DOWNLOAD_DIRECTORY = Path.home() / 'Downloads'


class MediaDownloader:
	def __init__(self) -> None:
		self.root = Tk()
		self.root.title('Media Downloader')
		self.root.resizable(False, False)
		self.output_queue: queue.Queue[str] = queue.Queue()

		Label(
			self.root,
			text='クリップボードの動画URLをダウンロードします',
			font=('', 12, 'bold'),
		).pack(anchor='w', padx=20, pady=(20, 0))
		Label(self.root, text=f'保存先: {DOWNLOAD_DIRECTORY}', fg='dim gray').pack(
			anchor='w', padx=20, pady=(6, 12)
		)
		self.download_button = Button(
			self.root,
			text='クリップボードからダウンロード',
			command=self.download_from_clipboard,
			height=2,
		)
		self.download_button.pack(fill='x', padx=20)
		self.output = ScrolledText(self.root, width=74, height=10, state=DISABLED, wrap='word')
		self.output.pack(fill='both', expand=True, padx=20, pady=(12, 20))

		self.root.after(100, self.flush_output)

	def run(self) -> None:
		self.root.mainloop()

	def download_from_clipboard(self) -> None:
		try:
			url = self.extract_clipboard_url()
		except ValueError as error:
			messagebox.showerror('Media Downloader', str(error))
			return

		self.download_button.config(state=DISABLED)
		self.append_output(f'URL: {url}')
		self.append_output('ダウンロードを開始します...')
		threading.Thread(target=self.download, args=(url,), daemon=True).start()

	def extract_clipboard_url(self) -> str:
		try:
			clipboard_text = self.root.clipboard_get()
		except TclError as error:
			raise ValueError('クリップボードからテキストを読み取れません。') from error

		match = URL_PATTERN.search(clipboard_text)
		if match is None:
			raise ValueError('クリップボード内にHTTPまたはHTTPSのURLが見つかりません。')

		url = match.group(0)
		if urlparse(url).scheme not in ('http', 'https'):
			raise ValueError('HTTPまたはHTTPSのURLをコピーしてください。')

		return url

	def download(self, url: str) -> None:
		if shutil.which('yt-dlp') is None:
			self.output_queue.put('エラー: yt-dlp がPATH上に見つかりません。yt-dlpをインストールしてから再実行してください。')
			self.output_queue.put('__DOWNLOAD_FAILED__')
			return

		try:
			DOWNLOAD_DIRECTORY.mkdir(parents=True, exist_ok=True)
			command = [
				'yt-dlp',
				'--embed-metadata',
				'--embed-thumbnail',
				'-P',
				str(DOWNLOAD_DIRECTORY),
				'--',
				url,
			]
			process = subprocess.Popen(
				command,
				stdout=subprocess.PIPE,
				stderr=subprocess.STDOUT,
				text=True,
				encoding='utf-8',
				errors='replace',
				creationflags=subprocess.CREATE_NO_WINDOW,
			)
			if process.stdout is not None:
				for line in process.stdout:
					self.output_queue.put(line.rstrip())

			if process.wait() == 0:
				self.output_queue.put('__DOWNLOAD_COMPLETED__')
			else:
				self.output_queue.put(f'エラー: yt-dlp が終了コード {process.returncode} で失敗しました。')
				self.output_queue.put('__DOWNLOAD_FAILED__')
		except OSError as error:
			self.output_queue.put(f'エラー: yt-dlp を起動できませんでした: {error}')
			self.output_queue.put('__DOWNLOAD_FAILED__')

	def flush_output(self) -> None:
		try:
			while True:
				line = self.output_queue.get_nowait()
				if line == '__DOWNLOAD_COMPLETED__':
					self.append_output('完了しました。')
					self.download_button.config(state=NORMAL)
				elif line == '__DOWNLOAD_FAILED__':
					self.download_button.config(state=NORMAL)
				else:
					self.append_output(line)
		except queue.Empty:
			pass
		finally:
			self.root.after(100, self.flush_output)

	def append_output(self, line: str) -> None:
		self.output.config(state=NORMAL)
		self.output.insert('end', f'{line}\n')
		self.output.see('end')
		self.output.config(state=DISABLED)


if __name__ == '__main__':
	MediaDownloader().run()
