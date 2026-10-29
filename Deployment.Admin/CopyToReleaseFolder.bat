rem xcopy ".\Debug\*.msi" "\\evergreen\Globeweigh\Releases"

robocopy ".\Debug" "\\evergreen\Globeweigh\Releases" "*.msi" /V