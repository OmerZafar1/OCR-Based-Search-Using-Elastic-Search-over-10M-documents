<#
.SYNOPSIS
    Generates test PDF files locally — no website or download needed.

.EXAMPLE
    .\Generate-TestPdfs.ps1 -Count 100

.EXAMPLE
    .\Generate-TestPdfs.ps1 -Count 100000 -OutputDirectory "C:\Users\Omer Zafar\Desktop\New folder (12)\Documents\loadtest"
#>
param(
    [int]$Count = 100,
    [string]$OutputDirectory = "./Documents/loadtest"
)

$Keywords = @("invoice", "contract", "payment", "report", "legal", "medical", "receipt", "proposal", "statement", "agreement")

function New-SimplePdfFile {
    param(
        [string]$Path,
        [string]$Title,
        [string]$Body
    )

    $line1 = ($Title -replace '\\', '\\' -replace '\(', '\(' -replace '\)', '\)')
    $line2 = ($Body -replace '\\', '\\' -replace '\(', '\(' -replace '\)', '\)')

    $streamContent = @"
BT
/F1 14 Tf
40 760 Td
($line1) Tj
0 -24 Td
/F1 11 Tf
($line2) Tj
ET
"@
    $streamContent = ($streamContent -replace "`r`n", "`n").Trim()
    $streamLength = [System.Text.Encoding]::ASCII.GetByteCount($streamContent)

    $sb = New-Object System.Text.StringBuilder
    $offsets = New-Object System.Collections.Generic.List[int]

    function Add-Object([string]$content) {
        $null = $offsets.Add($sb.Length)
        [void]$sb.Append($content)
        [void]$sb.Append("`n")
    }

    [void]$sb.Append("%PDF-1.4`n")
    Add-Object "1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj"
    Add-Object "2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj"
    Add-Object "3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >> endobj"
    Add-Object "4 0 obj << /Length $streamLength >> stream`n$streamContent`nendstream endobj"
    Add-Object "5 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> endobj"

    $xrefPos = $sb.Length
    [void]$sb.Append("xref`n")
    [void]$sb.Append("0 $($offsets.Count + 1)`n")
    [void]$sb.Append("0000000000 65535 f `n")
    foreach ($off in $offsets) {
        [void]$sb.Append(("{0:D10} 00000 n `n" -f $off))
    }
    [void]$sb.Append("trailer << /Size $($offsets.Count + 1) /Root 1 0 R >>`n")
    [void]$sb.Append("startxref`n$xrefPos`n%%EOF`n")

    $dir = Split-Path $Path -Parent
    if ($dir -and -not (Test-Path $dir)) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }

    [System.IO.File]::WriteAllText($Path, $sb.ToString(), [System.Text.Encoding]::ASCII)
}

Write-Host "Creating $Count test PDFs in:" -ForegroundColor Cyan
Write-Host "  $OutputDirectory"
Write-Host ""

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$sw = [System.Diagnostics.Stopwatch]::StartNew()

for ($i = 1; $i -le $Count; $i++) {
    $keyword = $Keywords[$i % $Keywords.Count]
    $title = "Document $i"
    $body = "Test document $i about $keyword. Reference REF-$($i.ToString('000000')). Amount $($i * 17)."
    $filePath = Join-Path $OutputDirectory ("doc_{0}.pdf" -f $i.ToString("000000"))

    New-SimplePdfFile -Path $filePath -Title $title -Body $body

    if ($i % 1000 -eq 0 -or $i -eq $Count) {
        $rate = if ($sw.Elapsed.TotalSeconds -gt 0) { [math]::Round($i / $sw.Elapsed.TotalSeconds) } else { $i }
        Write-Host "  $i / $Count  (~$rate files/sec)"
    }
}

$sw.Stop()
Write-Host ""
Write-Host "Done. Generated $Count PDFs in $([math]::Round($sw.Elapsed.TotalSeconds, 1)) seconds." -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Make sure API + Worker + Docker are running"
Write-Host "  2. Open http://localhost:5016 and use Bulk Index"
Write-Host "     OR run:"
Write-Host "     curl -X POST `"http://localhost:5016/api/admin/bulk-ingest?sourceDirectory=$OutputDirectory`""
