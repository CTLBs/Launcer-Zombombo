# Zombombo Güncelleyici

Windows uygulaması. Her açılışta [oyun build deposunun](https://github.com/CTLBs/zombombo-game-builds/releases) en son GitHub Release sürümünü kontrol eder. Yeni sürüm varsa `game.zip` dosyasını indirir, SHA-256 değerini doğrular ve seçilen klasöre kurar. Oyun başlatma işlevi yoktur.

## Kullanım

1. `ZombomboUpdater.exe` dosyasını açın.
2. Oyun dosyalarının kurulacağı klasörü seçin.
3. Uygulama hemen kontrol edip varsa build'i indirir. Sonraki açılışlarda otomatik kontrol eder.

Klasör ve depo seçimi kullanıcı bilgisayarında `%LOCALAPPDATA%\ZombomboUpdater\settings.json` içinde saklanır. Güncelleyici yalnızca daha önce kendisinin kurduğu ve yeni ZIP içinde yer almayan oyun dosyalarını siler; kayıt dosyaları gibi diğer dosyalara dokunmaz. Oyun açıksa kilitli dosyalar nedeniyle güncelleme hata verebilir; oyunu kapatıp uygulamayı yeniden açın.

## Build yayınlama

Oyun build klasörünün **içeriği** `game.zip` kökünde olmalı. ZIP içinde ayrıca bir üst klasör olmamalı. Release içinde tam olarak `game.zip` ve `game.zip.sha256` dosyaları bulunmalı; sürüm etiketi her yayın için farklı olmalı. Ön sürümler `latest` API'sine gelmediği için normal Release yayınlayın.

Depoya yazma yetkisi olan kişi Windows PowerShell'de şunu çalıştırabilir:

```powershell
.\scripts\Publish-GameBuild.ps1 -BuildDirectory 'C:\Oyun\Build' -Version 'v1.0.0'
```

Arkadaşınızın build yükleyebilmesi için [build deposuna](https://github.com/CTLBs/zombombo-game-builds) işbirlikçi olarak eklenmesi ve kendi GitHub hesabıyla `gh auth login` yapması gerekir. Aynı komutu çalıştırabilir. `gh release create` yeni sürümü yayınlar; oyuncular güncelleyiciyi sonraki açışlarında indirir.

## EXE üretme

.NET 10 SDK ile:

```powershell
dotnet publish .\src\ZombomboUpdater\ZombomboUpdater.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o .\publish\win-x64
```

`publish\win-x64\ZombomboUpdater.exe` kendi .NET çalışma zamanını içerir. `updater-config.json` varsayılan depoyu değiştirmenizi sağlar; EXE içinde de mevcut depo varsayılan olarak ayarlıdır.

## Geliştirme

```powershell
dotnet build .\ZombomboUpdater.slnx
dotnet run --project .\tests\ZombomboUpdater.Tests
```
