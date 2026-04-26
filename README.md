# FoLabelMaker

`FoLabelMaker` is a .NET 10 console solution for scanning D365 Finance and Operations metadata, finding hard-coded user-facing text, generating labels, producing change plans, applying those plans, translating labels through the OpenAI API, and reporting missing or improvable text.

## Projects

- `FoLabelMaker.Core`: all business logic.
- `FoLabelMaker.Cli`: thin command-line wrapper.

## Build

```powershell
dotnet build FOLabelMaker.slnx
```

## appsettings.json

The CLI loads `appsettings.json` during startup. It first checks the current working directory, then the app base directory.

CLI arguments override values from the file.

Example:

```json
{
  "LabelMaker": {
    "MetadataRootPath": "C:\\Dev\\Peritus Invoice Flow - FoLabels",
    "ModelName": null,
    "LabelPrefix": "@PTS",
    "BaseLanguage": "en-US",
    "TargetLanguages": ["nb-NO", "sv-SE"],
    "ReuseSimilarLabels": false,
    "OverwriteTranslations": false
  },
  "OpenAi": {
    "Model": "gpt-5-mini",
    "ApiKeyEnvironmentVariable": "OPENAI_API_KEY",
    "BaseUrl": "https://api.openai.com/v1/chat/completions",
    "CacheFilePath": ".fo-labelmaker-ai-cache.json"
  }
}
```

With that file in place, you can omit repeated arguments such as `--metadata-root`, `--label-prefix`, `--base-language`, and `--target-language`.

## Commands

```powershell
 dotnet run --project FoLabelMaker.Cli -- scan --metadata-root samples/SampleMetadata/Metadata --model SampleModel --label-prefix "@SMP" --output scan-report.json
 dotnet run --project FoLabelMaker.Cli -- plan --metadata-root samples/SampleMetadata/Metadata --model SampleModel --label-prefix "@SMP" --base-language en-US --output label-plan.json
dotnet run --project FoLabelMaker.Cli -- apply --metadata-root samples/SampleMetadata/Metadata --plan label-plan.json
dotnet run --project FoLabelMaker.Cli -- improve --metadata-root samples/SampleMetadata/Metadata --model SampleModel --output improvements.json
```

## OpenAI Translation

Set `OPENAI_API_KEY` before running `translate` or `plan --use-ai`.

```powershell
$env:OPENAI_API_KEY = "..."
 dotnet run --project FoLabelMaker.Cli -- translate --metadata-root samples/SampleMetadata/Metadata --model SampleModel --label-prefix "@SMP" --base-language en-US --target-language nb-NO --target-language sv-SE --use-ai
```

AI requests only send label text and small context values. Responses are cached locally in `.fo-labelmaker-ai-cache.json`.

## Sample Fixture

The sample metadata under `samples/SampleMetadata` contains hard-coded metadata text, X++ string literals, and a label file folder so `scan`, `plan`, and `apply` can be exercised safely.

## Notes

- `scan` and `plan` do not modify files.
- `apply` writes `.bak` backups and a manifest of changed files.
- Invalid XML is reported and returns a non-zero exit code.
- Existing label references starting with `@` are detected and not replaced.
