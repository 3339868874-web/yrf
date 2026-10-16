param([string]$PythonPath = 'python')
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
New-Item -ItemType Directory -Force -Path build,dist,'dist\package' | Out-Null
$ffmpegPath = & $PythonPath -c 'import imageio_ffmpeg;print(imageio_ffmpeg.get_ffmpeg_exe())'
if ($LASTEXITCODE -ne 0) { throw 'Install requirements-build.txt first.' }
Copy-Item -LiteralPath $ffmpegPath -Destination 'build\ffmpeg.exe' -Force
$env:PYTHONPATH = (Join-Path $PSScriptRoot 'src') + [IO.Path]::PathSeparator + $env:PYTHONPATH
& $PythonPath -m unittest discover -s tests -v
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
$ffmpegSpec = (Join-Path $PSScriptRoot 'build\ffmpeg.exe') + ';.'
& $PythonPath -m PyInstaller --noconfirm --onefile --console --name audio-worker --distpath build\engine --workpath build\pyinstaller --specpath build --paths $env:PYTHONPATH --exclude-module tkinter --exclude-module imageio_ffmpeg --add-binary $ffmpegSpec src\worker.py
if ($LASTEXITCODE -ne 0) { throw 'Engine build failed.' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler not found.' }
$resource = '/resource:' + (Join-Path $PSScriptRoot 'build\engine\audio-worker.exe') + ',audio-worker.exe'
$destination = '/out:' + (Join-Path $PSScriptRoot 'dist\听转音频转换器.exe')
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll $resource $destination (Join-Path $PSScriptRoot 'src\Window.cs')
if ($LASTEXITCODE -ne 0) { throw 'GUI build failed.' }
Copy-Item -LiteralPath 'dist\听转音频转换器.exe','使用说明.txt','THIRD_PARTY_NOTICES.txt','LICENSE' -Destination 'dist\package' -Force
Copy-Item -LiteralPath 'licenses' -Destination 'dist\package' -Recurse -Force
& $ffmpegPath -version | Set-Content -LiteralPath 'dist\package\licenses\ffmpeg-version.txt' -Encoding utf8
Compress-Archive -Path 'dist\package\*' -DestinationPath 'dist\听转-windows-x64.zip' -Force
Write-Output 'Build finished: dist\听转音频转换器.exe and dist\听转-windows-x64.zip'
