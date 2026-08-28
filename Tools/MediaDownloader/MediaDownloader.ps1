Add-Type -AssemblyName PresentationFramework

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$window = [Windows.Markup.XamlReader]::Load((New-Object System.Xml.XmlNodeReader ([xml]@'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        Title="Media Downloader" Height="280" Width="620" ResizeMode="NoResize"
        WindowStartupLocation="CenterScreen">
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="12" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="12" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>
        <StackPanel Grid.Row="0">
            <TextBlock Text="クリップボードの動画URLをダウンロードします" FontSize="16" FontWeight="SemiBold" />
            <TextBlock Name="DestinationText" Margin="0,6,0,0" Foreground="DimGray" TextWrapping="Wrap" />
        </StackPanel>
        <Button Name="DownloadButton" Grid.Row="2" Height="40" Content="クリップボードからダウンロード" />
        <TextBox Name="OutputText" Grid.Row="4" IsReadOnly="True" TextWrapping="Wrap"
                 VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled"
                 FontFamily="Consolas" FontSize="12" />
    </Grid>
</Window>
'@)))

$downloadButton = $window.FindName('DownloadButton')
$destinationText = $window.FindName('DestinationText')
$outputText = $window.FindName('OutputText')
$downloadDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
$downloadDirectory = Join-Path $downloadDirectory 'Downloads'
$destinationText.Text = "保存先: $downloadDirectory"

function Add-OutputLine([string]$text) {
	$window.Dispatcher.BeginInvoke([Action]{
		$outputText.AppendText("$text$([Environment]::NewLine)")
		$outputText.ScrollToEnd()
	}) | Out-Null
}

function Get-ClipboardUrl {
	$clipboardText = [Windows.Clipboard]::GetText()
	$urlMatch = [regex]::Match($clipboardText, 'https?://[^\s<>"'']+')
	if (-not $urlMatch.Success) {
		throw 'クリップボード内にHTTPまたはHTTPSのURLが見つかりません。'
	}

	$uri = [Uri]$urlMatch.Value
	if ($uri.Scheme -notin @([Uri]::UriSchemeHttp, [Uri]::UriSchemeHttps)) {
		throw 'HTTPまたはHTTPSのURLをコピーしてください。'
	}

	return $uri.AbsoluteUri
}

function Start-Download([string]$url) {
	if (-not (Get-Command yt-dlp -ErrorAction SilentlyContinue)) {
		throw 'yt-dlp がPATH上に見つかりません。yt-dlpをインストールしてから再実行してください。'
	}

	New-Item -ItemType Directory -Force -Path $downloadDirectory | Out-Null
	$processInfo = [System.Diagnostics.ProcessStartInfo]::new()
	$processInfo.FileName = 'yt-dlp'
	$processInfo.Arguments = "--embed-metadata --embed-thumbnail -P `"$downloadDirectory`" -- `"$url`""
	$processInfo.UseShellExecute = $false
	$processInfo.RedirectStandardOutput = $true
	$processInfo.RedirectStandardError = $true
	$processInfo.CreateNoWindow = $true

	$process = [System.Diagnostics.Process]::new()
	$process.StartInfo = $processInfo
	$process.add_OutputDataReceived([System.Diagnostics.DataReceivedEventHandler]{ param($sender, $event) if ($null -ne $event.Data) { Add-OutputLine $event.Data } })
	$process.add_ErrorDataReceived([System.Diagnostics.DataReceivedEventHandler]{ param($sender, $event) if ($null -ne $event.Data) { Add-OutputLine $event.Data } })
	$process.Start() | Out-Null
	$process.BeginOutputReadLine()
	$process.BeginErrorReadLine()
	$process.WaitForExit()
	$process.WaitForExit()

	if ($process.ExitCode -ne 0) {
		throw "yt-dlp が終了コード $($process.ExitCode) で失敗しました。"
	}
}

$downloadWorker = [System.ComponentModel.BackgroundWorker]::new()
$downloadWorker.add_DoWork({
	param($sender, $event)

	try {
		Start-Download ([string]$event.Argument)
		$event.Result = $true
	}
	catch {
		$event.Result = $_.Exception
	}
})
$downloadWorker.add_RunWorkerCompleted({
	param($sender, $event)

	if ($event.Result -is [Exception]) {
		Add-OutputLine "エラー: $($event.Result.Message)"
	}
	elseif ($event.Error) {
		Add-OutputLine "エラー: $($event.Error.Message)"
	}
	else {
		Add-OutputLine '完了しました。'
	}

	$downloadButton.IsEnabled = $true
})

$downloadButton.Add_Click({
	if ($downloadWorker.IsBusy) {
		return
	}

	$downloadButton.IsEnabled = $false
	$outputText.Clear()

	try {
		$url = Get-ClipboardUrl
		Add-OutputLine "URL: $url"
		Add-OutputLine 'ダウンロードを開始します...'
		$downloadWorker.RunWorkerAsync($url)
	}
	catch {
		Add-OutputLine "エラー: $($_.Exception.Message)"
		$downloadButton.IsEnabled = $true
	}
})

$window.ShowDialog() | Out-Null
