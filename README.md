# FoLabelMaker

`FoLabelMaker` is a .NET 10 command-line tool for D365 Finance and Operations label maintenance.

It scans FO metadata and X++ source, finds hard-coded user-facing text, creates a safe change plan, updates label files, applies replacements, generates JSON and HTML reports, and can translate labels through OpenAI.

## Projects

- `FoLabelMaker.Core`
  All business logic.
- `FoLabelMaker.Cli`
  Thin command-line wrapper.

## Executable

The built executable is:

```text
FoLabelMaker.exe
```

Typical build output location:

```text
FoLabelMaker.Cli\bin\Debug\net10.0\FoLabelMaker.exe
```

## Core Idea

The tool is intentionally split into separate responsibilities:

1. `scan`
   Inspect metadata and report findings.
2. `plan`
   Build a safe plan for label replacements and label-file additions.
3. `apply`
   Apply a previously created plan.
4. `translate`
   Translate label files.
5. `improve`
   Suggest wording and formatting improvements.

Translation is separate from scan, plan, and apply.

## Safe Defaults

- `scan` does not change metadata.
- `plan` does not change metadata.
- `apply` only changes files from an explicit plan file.
- modified metadata and label files get `.bak` backups.
- `apply` writes a changed-files manifest.

## Build

```powershell
dotnet build FOLabelMaker.slnx
```

## Command Style

The CLI accepts single-dash long options.

Examples:

```powershell
FoLabelMaker scan -model MyModel
FoLabelMaker plan -model MyModel
FoLabelMaker apply -plan mymodel
FoLabelMaker translate -model MyModel -target-lang no -use-ai
FoLabelMaker improve -model MyModel
```

Positional model names are also supported for commands that operate on a model.

Examples:

```powershell
FoLabelMaker scan MyModel
FoLabelMaker plan MyModel
FoLabelMaker translate MyModel -target-lang no -use-ai
```

## Working Root and Path Resolution

### If you pass `-metadata-root`

That path is treated as the target working root.

It can be:

- a repository root
- a metadata root
- an exact model path

### If you do not pass `-metadata-root`

The tool assumes the current directory is the working root.

So if you are already standing in the target repo root, this works:

```powershell
FoLabelMaker plan MyModel
```

### Model discovery

If the working root contains multiple models, pass a model name.

The tool supports:

- exact model matching
- case-insensitive matching
- case-insensitive contains matching when the result is unambiguous

### Ignored technical trees

The tool intentionally avoids technical duplicate metadata trees such as:

```text
XppMetadata
```

## Report Files

### Relative output paths

Relative output paths are written to the target working root.

### Companion HTML reports

Whenever a JSON report is written, a companion HTML report is also written.

Examples:

```text
mymodel-scan.json
mymodel-scan-report.html

mymodel-plan.json
mymodel-plan-report.html
```

The HTML report contains:

- a readable report title
- created date/time
- summary cards
- detailed tables
- validation details

## Default Output Names

If `-output` is omitted, default names are based on the resolved model name.

Examples:

- `FoLabelMaker scan -model MyModel`
  writes:
  - `mymodel-scan.json`
  - `mymodel-scan-report.html`

- `FoLabelMaker plan -model MyModel`
  writes:
  - `mymodel-plan.json`
  - `mymodel-plan-report.html`

- `FoLabelMaker improve -model MyModel`
  writes:
  - `mymodel-improvements.json`
  - `mymodel-improvements-report.html`

If no model can be resolved, fallback names use `report`.

## Commands

### `scan`

Scans metadata and reports:

- detected user-facing text candidates
- ignored candidates with reasons
- missing text proposals
- improvement suggestions
- validation errors

Examples:

```powershell
FoLabelMaker scan -model MyModel
FoLabelMaker scan MyModel
FoLabelMaker scan -metadata-root "C:\Dev\MyRepo" -model MyModel
```

### `plan`

Creates a JSON plan of:

- metadata replacements
- X++ string replacements
- label-file additions

The plan also includes:

- missing text proposals
- ignored candidates
- validation errors

Examples:

```powershell
FoLabelMaker plan -model MyModel
FoLabelMaker plan MyModel
FoLabelMaker plan -metadata-root "C:\Dev\MyRepo" -model MyModel -output custom-plan.json
```

`plan` does not translate labels.

### `apply`

Applies a previously generated plan.

Examples:

```powershell
FoLabelMaker apply -plan mymodel-plan.json
FoLabelMaker apply -plan mymodel
```

If `-plan` is given without an extension and without a path, the tool assumes the default plan file name:

```text
<value>-plan.json
```

So this:

```powershell
FoLabelMaker apply -plan mymodel
```

resolves to:

```text
mymodel-plan.json
```

Apply behavior:

- updates metadata files from the plan
- updates base-language label files
- creates `.bak` backups
- writes `fo-labelmaker-apply-manifest.json`

`apply` does not perform translation.

### `translate`

Creates or updates translated label files using OpenAI.

Examples:

```powershell
FoLabelMaker translate -model MyModel -target-lang no -use-ai
FoLabelMaker translate MyModel -target-lang no -use-ai
FoLabelMaker translate -metadata-root "C:\Dev\MyRepo" -model MyModel -base-lang en -target-lang sv
```

Translate behavior:

- reads the base-language label file as source
- only sends label text and minimal context
- preserves placeholders like `%1` and `{0}`
- validates placeholder and line-break preservation
- writes translated label files using FO label-file structure

This is the only command that accepts translation-specific options like:

- `-target-lang`
- `-target-language`
- `-use-ai`
- overwrite translation settings

### `improve`

Produces text-improvement suggestions without changing metadata.

Examples:

```powershell
FoLabelMaker improve -model MyModel
FoLabelMaker improve MyModel
```

## Typical Workflows

### Workflow 1: Scan and plan

```powershell
FoLabelMaker scan MyModel
FoLabelMaker plan MyModel
```

### Workflow 2: Apply a plan

```powershell
FoLabelMaker apply -plan mymodel
```

### Workflow 3: Translate after base labels exist

```powershell
FoLabelMaker plan MyModel
FoLabelMaker apply -plan mymodel
FoLabelMaker translate MyModel -target-lang no -use-ai
```

## Label File Behavior

### If label files already exist

The tool tries to reuse the existing D365 FO label-file structure.

Typical structure:

```text
AxLabelFile\MyLabels_en-US.xml
AxLabelFile\LabelResources\en-US\MyLabels.en-US.label.txt
```

In that case it should:

- add to the existing label text file
- use the existing label file ID in references
- continue the existing label ID sequence

### If no label file exists yet

The tool creates a model-local FO label-file structure named after the model.

Example pattern:

```text
AxLabelFile\MyModel_en-US.xml
AxLabelFile\LabelResources\en-US\MyModel.en-US.label.txt
```

For translations the same structure is used:

```text
AxLabelFile\MyModel_nb-NO.xml
AxLabelFile\LabelResources\nb-NO\MyModel.nb-NO.label.txt
```

## AI Configuration

The CLI loads `appsettings.json` at startup.

Lookup order:

1. current working directory
2. application base directory

Example:

```json
{
  "LabelMaker": {
    "MetadataRootPath": "C:\\Dev\\MyRepo",
    "ModelName": "MyModel",
    "LabelPrefix": "@MY",
    "BaseLanguage": "en-US",
    "TargetLanguages": ["nb-NO"],
    "UseAi": false,
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
3. built-in defaults

OpenAI authentication precedence:

1. `OpenAi.ApiKey`
2. environment variable named by `OpenAi.ApiKeyEnvironmentVariable`

## Language Shortcuts

The CLI supports common language shortcuts and normalizes them.

Examples:

- `en` -> `en-US`
- `en-us` -> `en-US`
- `no` -> `nb-NO`
- `nb` -> `nb-NO`
- `sv` -> `sv-SE`
- `da` -> `da-DK`
- `th` -> `th-TH`

Examples:

```powershell
FoLabelMaker translate MyModel -target-lang no -use-ai
FoLabelMaker translate MyModel -base-lang en -target-lang sv
```

## What Gets Classified as User-Facing

Examples normally treated as user-facing:

- labels
- captions
- help text
- menu item text
- button text
- error/warning text
- form/report text

Examples normally ignored:

- existing label references such as `@MyFile:MyLabel123`
- URLs
- file paths
- GUIDs
- SQL fragments
- placeholders-only strings like `%1`
- file extensions like `.pfx`
- technical identifiers in code

## Reuse Behavior

The reports distinguish between:

### Existing labels reused

The exact text already exists in a model label file.

### Duplicate texts consolidated

The same new text appears multiple times in the current scan or plan, so those occurrences share one label ID.

## Missing Text Proposals

The tool reports missing labels and captions for important FO elements.

Examples of generated proposals:

- form captions
- field labels
- EDT labels
- menu item labels
- report labels

These proposals are reported, not silently applied.

## Translation Progress Feedback

During `translate`, the tool prints progress such as:

- preparing translation plan
- resolved model
- base language
- target languages
- number of translation requests
- cache hits
- OpenAI request start
- OpenAI response status
- persistence of translated label files

This is intended to make long-running translation steps visible instead of appearing stalled.

## Exit Codes

- `0`
  success
- `1`
  usage or argument problem
- `2`
  validation or runtime failure

## Troubleshooting

### `Missing required value for option: -plan`

`-plan` is only an option for the `apply` command.

Wrong:

```powershell
FoLabelMaker scan -model MyModel -plan
```

Right:

```powershell
FoLabelMaker plan MyModel
FoLabelMaker apply -plan mymodel
```

### `Detected 0 candidates`

Possible reasons:

- the model was already labelized by a previous apply run
- the remaining strings are classified as technical
- the wrong path or wrong model was used

### `429 Too Many Requests`

This comes from OpenAI throttling or quota limits.

Current behavior:

- translation stops on the API failure
- no retry/backoff is implemented yet

### Default report name is not what you expected

Default names are based on the resolved model name.

Examples:

- `FoLabelMaker scan MyModel`
  -> `mymodel-scan.json`
- `FoLabelMaker plan MyModel`
  -> `mymodel-plan.json`

If no model can be resolved, the fallback prefix is `report`.

## Current Limitations

- no automatic retry/backoff for OpenAI `429`
- missing-text proposals are not yet automatically converted into applyable changes
- translation is intentionally separate from scan, plan, and apply

## Quick Examples

```powershell
FoLabelMaker scan MyModel
FoLabelMaker plan MyModel
FoLabelMaker apply -plan mymodel
FoLabelMaker translate MyModel -target-lang no -use-ai
FoLabelMaker improve MyModel
```
