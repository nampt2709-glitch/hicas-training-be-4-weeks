# Requires k6 on PATH. Run from this folder so k6/Results paths resolve correctly.
Set-Location $PSScriptRoot
k6 run .\apartment-api.js
exit $LASTEXITCODE
