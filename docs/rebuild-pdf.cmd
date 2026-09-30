@echo off
REM Rebuilds the PDF from the HTML source. Run from the repo root.
REM The HTML is the source of truth; edit it, then re-run this.
"C:\Program Files\Google\Chrome\Application\chrome.exe" --headless --disable-gpu --no-pdf-header-footer ^
  --print-to-pdf="%CD%\docs\Molargo-Using-the-system.pdf" ^
  "file:///%CD:\=/%/docs/Molargo-Using-the-system.html"
