# A plain Win32 text window standing in for "the app the user was typing in".
# Started by Start-TargetWindow; runs until closed or its process is killed.
param([string]$Title = 'CyrFlip layout target')

Add-Type -AssemblyName System.Windows.Forms
$f = New-Object System.Windows.Forms.Form
$f.Text = $Title
$f.Width = 520; $f.Height = 220
$f.StartPosition = 'CenterScreen'
$tb = New-Object System.Windows.Forms.TextBox
$tb.Dock = 'Fill'; $tb.Multiline = $true
$f.Controls.Add($tb)
$f.Add_Shown({ $tb.Focus() | Out-Null })
[System.Windows.Forms.Application]::Run($f)
