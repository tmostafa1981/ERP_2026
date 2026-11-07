$lines = Get-Content 'o:\Tamer\ERP_2026\ERP\menu_settings_snippet.txt'
$lines | Where-Object { $_ -match '^(61[0-9]|62[0-9]|63[0-9]|64[0-9]|65[0-9]):' } | ForEach-Object { Write-Output $_ }
