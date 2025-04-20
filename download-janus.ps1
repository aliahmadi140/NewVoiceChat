$janusUrl = "https://raw.githubusercontent.com/meetecho/janus-gateway/master/html/janus.js"
$outputPath = "NewVoiceChat.Client/wwwroot/js/janus-lib.js"

Write-Host "Downloading Janus JavaScript library..."
Invoke-WebRequest -Uri $janusUrl -OutFile $outputPath
Write-Host "Download complete. File saved to: $outputPath" 