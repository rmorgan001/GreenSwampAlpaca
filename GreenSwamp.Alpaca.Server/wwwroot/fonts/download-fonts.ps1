$fontPath = "T:\source\repos\GreenSwampAlpaca\GreenSwamp.Alpaca.Server\wwwroot\fonts\MaterialSymbolsOutlined.woff2"
$bytes = [System.IO.File]::ReadAllBytes($fontPath)

foreach ($name in @("travel_explore", "home", "settings", "telescope")) {
    $search = [System.Text.Encoding]::UTF8.GetBytes($name)
    $found = $false
    for ($i = 0; $i -lt ($bytes.Length - $search.Length); $i++) {
        $match = $true
        for ($j = 0; $j -lt $search.Length; $j++) {
            if ($bytes[$i + $j] -ne $search[$j]) { $match = $false; break }
        }
        if ($match) { $found = $true; break }
    }
    if ($found) { Write-Host "IN FONT:     $name" } else { Write-Host "NOT IN FONT: $name" }
}
