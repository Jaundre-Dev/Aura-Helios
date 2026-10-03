# Measuring document products (P3 gate)

The plan's P3 gate needs, for each document product, "an approved representative dataset and written field-specific accuracy, review-rate and latency targets", with measured results published. `helios-evaluate` produces those measurements. It runs the same extractor code the API runs, in process, on the machine where the dataset lives. It uploads nothing and stores nothing in HELIOS.

## Rules for datasets

- Use only documents you are lawfully allowed to process for this purpose, and keep them **outside this repository**. The tool refuses any folder inside a HELIOS checkout.
- Labels are your ground truth: label what a careful person reads on the document, not what the extractor returns.
- Reports leave out document values by default and list only paths, counts and file names. `--include-values` adds expected and extracted values to mismatches; handle such a report like the documents themselves.

## Dataset layout

```
my-dataset/
  manifest.json
  inv-0001.pdf
  inv-0002.pdf
  …
```

`manifest.json`:

```json
{
  "name": "pilot-invoices-2026-10",
  "product": "documents.invoice",
  "version": "1",
  "caseSensitive": false,
  "targets": {
    "fieldAccuracy": { "fields.total": 0.98, "fields.invoiceNumber": 0.97 },
    "defaultFieldAccuracy": 0.9,
    "maxReviewRate": 0.2,
    "maxP95Milliseconds": 2000,
    "maxInventedValues": 0
  },
  "documents": [
    {
      "file": "inv-0001.pdf",
      "expected": {
        "fields.invoiceNumber": "INV-2026-0042",
        "fields.total": 11500.00,
        "fields.dueDate": "2026-10-15",
        "fields.customerVatNumber": null,
        "lineItems[0].amount": 2500.00
      }
    }
  ]
}
```

- **Paths** are the ones reviewers use for corrections:
  - `fields.<name>` for a document field;
  - `<list>[i].<property>` for a list item (`transactions[3].direction`);
  - a top-level name such as `type` for `documents.classify`.
- **Expected `null`** means the value is not on the document. Any value extracted there is counted as *invented*. By default, one invented value fails the gate.
- **Comparison rules:**
  - Numbers match within 0.005.
  - Text matches after collapsing whitespace, ignoring case unless `caseSensitive` is true.
  - Dates are ISO strings (`2026-10-15`).
  - Lists match item by item.
- **Each labelled value is counted once**, as correct, wrong, missed (expected but not extracted) or invented.

## Running

```bash
dotnet run --project src/Helios.Evaluation -- "D:\datasets\pilot-invoices-2026-10" --out "D:\datasets\reports\invoices-run-1"
```

The tool writes `report.json` and `report.md`. Exit codes:
- `0`: every written target was met.
- `2`: targets were missed or none were written.
- `1`: the run could not start.

Timings exclude one untimed warm-up document and cover text-layer reading plus extraction; they do not include upload, scanning or queueing.

## What the result does and does not establish

A passing report shows that this product version met these targets on this dataset. It does not show the version will meet them on documents unlike the dataset, so record how the dataset was selected (sources, date range, layouts, how many scans were excluded). v1 products read native PDF text layers only: scanned documents come back as unreadable and count against accuracy wherever they are labelled. Moving a product beyond Sandbox remains a decision for the owner, through the platform release-state endpoint, after reviewing the report.
