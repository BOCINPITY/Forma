$ErrorActionPreference = 'Stop'

# Hosted Windows runners start at a small resolution that constrains native WPF
# window sizes. Change only the disposable CI session, never a developer's display.
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'This script is only for GitHub-hosted CI sessions.' }
Set-DisplayResolution -Width 1920 -Height 1080 -Force
