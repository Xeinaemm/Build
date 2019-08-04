Import-Module WebAdministration
Push-Location
Set-Location IIS:\SslBindings
$c = New-SelfSignedCertificate -DnsName "localhost" -KeyAlgorithm RSA -KeyLength 2048 -NotAfter (Get-Date).AddYears(2)
$c | New-Item 0.0.0.0!443
Move-Item (Join-Path Cert:\LocalMachine\My $c.Thumbprint) -Destination Cert:\LocalMachine\Root
Pop-Location