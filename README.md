# FoLabelMaker

`FoLabelMaker` is a .NET 10 command-line tool for D365 Finance and Operations label work.

It scans FO metadata and X++ source, finds hard-coded user-facing text, creates a safe change plan, updates label files, applies replacements, generates HTML/JSON reports, and can translate labels with OpenAI.

## Projects

- `FoLabelMaker.Core`
  All business logic.
- `FoLabelMaker.Cli`
  Thin command-line wrapper.

## Output

The built executable is:

```text
FoLabelMaker.exe
```

Typical build output location:

```text
FoLabelMaker.Cli\bin\Debug\net10.0\FoLabelMaker.exe
```

## What It Does

`FoLabelMaker` currently supports these main workflows:

1. `scan`
   Reads metadata and reports hard-coded or missing text.
2. `plan`
   Creates a JSON change plan and companion HTML report.
3. `apply`
   Applies a previously created plan to metadata and label files.
4. `translate`
   Creates or updates translated label files using OpenAI.
5. `improve`
   Produces suggestions for awkward or inconsistent text.

## Safe Defaults

- `scan` does not change metadata.
- `plan` does not change metadata.
- `apply` only changes files from an explicit plan file.
- backups are created for modified metadata and label files.
- a changed-files manifest is written during apply.

## Build

```powershell
dotnet build FOLabelMaker.slnx
```

## Basic Command Style

The CLI supports single-dash long options.

Examples:

```powershell
FoLabelMaker scan -model ocr
FoLabelMaker plan -model ocr
FoLabelMaker apply -plan ocr-plan.json
FoLabelMaker translate -model ocr -target-language nb-NO -use-ai
```

## How Path Resolution Works

This is important.

### 1. If you pass `-metadata-root`

That path is treated as the target working root for the run.

Examples:

- repo root:
  `C:\Dev\Peritus OCR`
- metadata root:
  `C:\Dev\Peritus OCR\Metadata`
- exact model path:
  `C:\Dev\Peritus OCR\Metadata\PTSOCR\PTSOCR`

### 2. If you do not pass `-metadata-root`

The tool assumes the current directory is the working root.

So if you are already standing in:

```text
C:\Dev\Peritus OCR
```

then this is valid:

```powershell
FoLabelMaker scan -model ocr
```

### 3. Model discovery

If you pass a repo root, the tool will try to find the model automatically.

Examples:

- `-model PTSOCR`
- `-model ocr`

The tool supports case-insensitive contains matching when there is only one unambiguous match.

### 4. Ignored metadata trees

The tool intentionally stays away from technical duplicate metadata trees like:

```text
XppMetadata
```

## Report Output Rules

### Relative report paths

Relative report paths are written into the requested target root.

Example:

```powershell
FoLabelMaker scan -metadata-root "C:\Dev\Peritus OCR" -output ocr-scan-report.json
```

This creates:

```text
C:\Dev\Peritus OCR\ocr-scan-report.json
C:\Dev\Peritus OCR\ocr-scan-report.html
```

### If you do not pass `-output`

Defaults are used:

- `scan` -> `scan-report.json`
- `plan` -> `plan-report.json`
- `improve` -> `improvements.json`

Each JSON report also gets a companion HTML report beside it.

Example:

```text
scan-report.json
scan-report.html
```

## Commands

### `scan`

Scans metadata and produces a report.

Example:

```powershell
FoLabelMaker scan -metadata-root "C:\Dev\Peritus OCR" -model ocr
```

Typical outputs:

- `scan-report.json`
- `scan-report.html`

The scan report includes:

- scanned files
- detected candidates
- ignored candidates with reasons
- missing text proposals
- improvement suggestions
- validation errors

### `plan`

Creates a JSON plan of replacements and label-file additions.

Example:

```powershell
FoLabelMaker plan -metadata-root "C:\Dev\Peritus OCR" -model ocr
```

Typical outputs:

- `plan-report.json`
- `plan-report.html`

The plan includes:

- planned metadata replacements
- planned X++ replacements
- planned label-file additions
- missing text proposals
- ignored candidates
- validation errors

### `apply`

Applies a previously generated plan.

Example:

```powershell
FoLabelMaker apply -metadata-root "C:\Dev\Peritus OCR" -plan ocr-plan.json
```

Apply behavior:

- modifies metadata files from the plan
- creates `.bak` backups
- updates label files
- writes `fo-labelmaker-apply-manifest.json`

### `translate`

Creates or updates translated label files using OpenAI.

Example:

```powershell
FoLabelMaker translate -metadata-root "C:\Dev\Peritus OCR" -model ocr -base-language en-US -target-language nb-NO -use-ai
```

Translate behavior:

- uses the base-language label file as source
- only sends label text and minimal context
- preserves placeholders like `%1` and `{0}`
- validates placeholder and line-break preservation
- writes translated label files in FO layout

### `improve`

Produces text-improvement suggestions without changing files.

Example:

```powershell
FoLabelMaker improve -metadata-root "C:\Dev\Peritus OCR" -model ocr
```

## Common Workflows

### Workflow 1: Already in the repo root

If your shell is already in the repo root:

```text
C:\Dev\Peritus OCR
```

you can run:

```powershell
FoLabelMaker scan -model ocr
FoLabelMaker plan -model ocr
```

### Workflow 2: Full explicit path

```powershell
FoLabelMaker scan -metadata-root "C:\Dev\Peritus OCR" -model ocr -output ocr-scan-report.json
FoLabelMaker plan -metadata-root "C:\Dev\Peritus OCR" -model ocr -output ocr-plan.json
FoLabelMaker apply -metadata-root "C:\Dev\Peritus OCR" -plan ocr-plan.json
```

### Workflow 3: Create base labels, then translate

```powershell
FoLabelMaker plan -metadata-root "C:\Dev\Peritus OCR" -model ocr -output ocr-plan.json
FoLabelMaker apply -metadata-root "C:\Dev\Peritus OCR" -plan ocr-plan.json
FoLabelMaker translate -metadata-root "C:\Dev\Peritus OCR" -model ocr -base-language en-US -target-language nb-NO -use-ai
```

## Label File Behavior

### If the model already has label files

The tool tries to reuse the existing D365 FO label-file structure.

Example existing structure:

```text
AxLabelFile\PTSEHFLabels_en-US.xml
AxLabelFile\LabelResources\en-US\PTSEHFLabels.en-US.label.txt
```

In that case the tool should:

- add to the existing label text file
- use the existing label file ID in references
- continue the existing label ID sequence

### If the model does not yet have label files

The tool creates a D365-style structure named after the model.

Example for model `PTSOCR`:

```text
AxLabelFile\PTSOCR_en-US.xml
AxLabelFile\LabelResources\en-US\PTSOCR.en-US.label.txt
```

For translations, the same pattern is used:

```text
AxLabelFile\PTSOCR_nb-NO.xml
AxLabelFile\LabelResources\nb-NO\PTSOCR.nb-NO.label.txt
```

## AI Configuration

The tool supports OpenAI configuration in `appsettings.json`.

Example:

```json
{
  "LabelMaker": {
    "MetadataRootPath": "C:\\Dev\\Peritus OCR",
    "ModelName": "PTSOCR",
    "LabelPrefix": "@PTSOCR",
    "BaseLanguage": "en-US",
    "TargetLanguages": ["nb-NO"],
    "ReuseSimilarLabels": false,
    "OverwriteTranslations": false
  },
  "OpenAi": {
    "ApiKey": "your-real-key-here",
    "Model": "gpt-5-mini",
    "ApiKeyEnvironmentVariable": "OPENAI_API_KEY",
    "BaseUrl": "https://api.openai.com/v1/chat/completions",
    "CacheFilePath": ".fo-labelmaker-ai-cache.json"
  }
}
```

Precedence:

1. command-line option
2. `appsettings.json`
3. current-directory defaults where applicable

For OpenAI authentication:

1. `OpenAi.ApiKey`
2. environment variable named by `OpenAi.ApiKeyEnvironmentVariable`

## What Gets Classified as User-Facing

Examples that are normally treated as user-facing:

- labels
- captions
- help text
- button text
- menu item text
- dialog/error/warning text
- form/report text

Examples that are normally ignored:

- existing labels like `@MyFile:MyLabel123`
- URLs
- file paths
- GUIDs
- SQL fragments
- placeholders-only text like `%1`
- file extensions like `.pfx`
- technical identifiers in code

## Reuse Behavior

The tool distinguishes between two reuse cases.

### Existing labels reused

The exact text already exists in the model label file.

### Duplicate texts consolidated

The same new text appears multiple times in the current scan/plan.

Those occurrences should share the same new label ID.

## Missing Text Proposals

The tool reports missing labels and captions for important FO elements.

Example:

- `PTSOCRParameters`
  -> `OCR Parameters`

These proposals are reported but not silently applied.

## HTML Reports

Every JSON report written by the tool also gets a companion HTML report.

Example:

```text
ocr-plan.json
ocr-plan.html
```

The HTML report contains:

- a sensible report title
- the report file name
- the created date/time
- summary cards
- tables for detected or planned items
- validation details

## Exit Codes

- `0`
  success
- `1`
  usage or argument problem
- `2`
  validation failure or runtime failure

## Troubleshooting

### `Missing required value for option: -plan`

`-plan` is only an option for the `apply` command.

Wrong:

```powershell
FoLabelMaker scan -model ocr -plan
```

Right:

```powershell
FoLabelMaker plan -model ocr
FoLabelMaker apply -plan ocr-plan.json
```

### `Detected 0 candidates`

Possible reasons:

- the metadata was already labelized by a previous apply run
- the remaining strings are classified as technical
- you scanned the wrong path or wrong model

### `429 Too Many Requests`

This comes from OpenAI rate limiting or quota limits.

Current behavior:

- translation fails immediately
- no retry/backoff yet

### No report file created where expected

Relative outputs are written to the requested target root.

Examples:

- if target root is `C:\Dev\Peritus OCR`
- then `scan-report.json` is written to `C:\Dev\Peritus OCR`

## Current Known Limitations

- no automatic retry/backoff for OpenAI `429`
- missing text proposals are reported but not yet turned into applyable changes automatically
- translation depends on successful OpenAI access at runtime

## Sample Commands

```powershell
FoLabelMaker scan -model ocr
FoLabelMaker plan -model ocr
FoLabelMaker apply -plan plan-report.json
FoLabelMaker translate -model ocr -base-language en-US -target-language nb-NO -use-ai
FoLabelMaker improve -model ocr
```
