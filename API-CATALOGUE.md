# HELIOS API catalogue

Planning catalogue, 2026-09-28. All entries are proposals, not currently implemented products. Buyers and priorities are hypotheses for validation. See HELIOS-IMPLEMENTATION-PLAN.md for release gates and billing rules.

Delivery: Build = deterministic first-party software; AI = approved model/engine; Hybrid = software plus AI or external data; Partner = contracted specialist/authorised data access. Priority A = first document/foundation release; B = initial contracted verification; C = expansion after paid demand; D = later specialist/restricted product. Unit prices must be approved separately. Every billed unit has documented bounds and retry semantics.

## Documents

| # | Product slug | Service and buyer value | Delivery | Priority | Billing unit |
| --- | --- | --- | --- | --- | --- |
| 1 | ocr.general | Scans to text for admin and software platforms | AI | A | Page |
| 2 | documents.sa-id | SA identity document fields for onboarding | Hybrid | A | Document, stated sides |
| 3 | documents.passport | Passport fields/MRZ for onboarding | Hybrid | C | Document |
| 4 | documents.driving-licence | Licence fields for fleets/rentals/HR | Hybrid | C | Document |
| 5 | documents.invoice | Supplier, totals, VAT and line items for accounting | Hybrid | A | Document + excess pages |
| 6 | documents.receipt | Expense capture for bookkeeping | Hybrid | C | Receipt |
| 7 | documents.bank-statement | Structured transactions for accounting/property/finance | Hybrid | A | Document + excess pages |
| 8 | documents.payslip | Pay/employer/deduction capture for property/HR | Hybrid | A | Document |
| 9 | documents.proof-of-address | Name/address/issuer/date capture for onboarding | Hybrid | A | Document |
| 10 | documents.purchase-order | Procurement capture | Hybrid | C | Document + excess pages |
| 11 | documents.proof-of-delivery | References/dates/quantities for logistics | Hybrid | C | Document |
| 12 | documents.classify | Type detection and routing for document workflows | AI | A | Document/page band |
| 13 | documents.custom-extract | Customer-defined schema extraction | AI | C | Bounded document/token band |
| 14 | documents.quality | Blur/glare/crop/readability signals | Hybrid | C | Image/page |
| 15 | documents.consistency | Compare names, dates and amounts across supplied files | Hybrid | C | Bounded document set |
| 16 | documents.tamper-signals | Specialist manipulation indicators for review | Partner/hybrid | D | Document |

Document extraction does not establish authenticity. Quality and tampering signals require measured validation. Statement arithmetic checks are not credit decisions. Bounded inputs and evidence locations are mandatory for billable extraction.

## Identity and applicant onboarding

| # | Product slug | Service and buyer value | Delivery | Priority | Billing unit |
| --- | --- | --- | --- | --- | --- |
| 17 | identity.sa-id-validate | Format/checksum validation to catch input mistakes | Build | A | Request or included utility |
| 18 | identity.verify | Official-source check through authorised access | Partner | D | Completed lookup |
| 19 | identity.face-compare | Selfie-to-document comparison for onboarding | Partner | B | Comparison |
| 20 | identity.liveness-passive | Specialist presentation-attack detection | Partner | B | Session with attempt allowance |
| 21 | identity.liveness-active | Guided capture challenges where justified | Partner | C | Session with attempt allowance |
| 22 | identity.age-threshold | Minimal-disclosure age eligibility from an approved source | Hybrid | C | Check |
| 23 | identity.address-compare | Compare submitted/extracted addresses | Hybrid | C | Comparison |
| 24 | identity.document-authenticity | Specialist document-security checks | Partner | D | Document |
| 25 | identity.consent-evidence | Purpose/notice/time evidence for customer workflow | Build | C | Workflow event or included |
| 26 | identity.hosted-session | Hosted capture and configured check sequence | Hybrid | C | Itemised session/checks |

Passive liveness and face comparison require approved specialist integration; active liveness is not automatically superior. ID checksum does not query Home Affairs. Consent evidence is a record, not a guarantee of lawful processing. Never manufacture positive sandbox checks in live mode.

## Company, supplier and financial information

| # | Product slug | Service and buyer value | Delivery | Priority | Billing unit |
| --- | --- | --- | --- | --- | --- |
| 27 | company.lookup | Registration lookup for suppliers/merchants | Partner | B | Lookup |
| 28 | company.status | Current company status | Partner | C | Lookup |
| 29 | company.directors | Permitted director details and verification | Partner | D | Lookup |
| 30 | company.beneficial-owners | Beneficial ownership where access is permitted | Partner | D | Lookup |
| 31 | banking.account-verify | Account/holder match to reduce payment errors | Partner | B | Completed check |
| 32 | screening.sanctions | Licensed list screening for due diligence | Partner | D | Subject/search |
| 33 | screening.pep | PEP screening and review evidence | Partner | D | Subject/search |
| 34 | screening.adverse-media | Relevant sourced candidate matches for review | Hybrid | D | Bounded search |
| 35 | company.vat-verify | Registration check from authorised source | Partner | D | Check |
| 36 | company.tax-status | Authorised taxpayer-permission workflow | Partner | D | Check |
| 37 | documents.bbee-extract | Certificate/affidavit fields and dates for procurement | Hybrid | C | Document |
| 38 | credit.report | Licensed credit information for permitted use | Partner | D | Report |
| 39 | qualifications.verify | Authorised credential checking for recruitment | Partner | D | Qualification/check |
| 40 | suppliers.monitor | Permitted changes and alerts for approved entities | Hybrid | D | Entity/month |

Availability, redistribution rights, lawful purposes and field coverage are provider-dependent. Do not scrape protected sources or imply government approval. B-BBEE extraction is not verification or certification. Screening results are candidate matches with source/date/review state, not automatic adverse decisions.

## Business utilities and communication

| # | Product slug | Service and buyer value | Delivery | Priority | Billing unit |
| --- | --- | --- | --- | --- | --- |
| 41 | pdf.generate | Render invoices/reports from bounded HTML/template | Build | C | Document/page band |
| 42 | pdf.transform | Merge/split/compress for workflow tools | Build | C | Operation/page band |
| 43 | documents.generate | Template-based letters/agreements/packs | Build | C | Document |
| 44 | signatures.envelope | Signature workflow and evidence via specialist | Partner | D | Envelope/recipient |
| 45 | codes.barcode-qr | Read/generate codes for logistics/inventory | Build | C | Image/request |
| 46 | data.validate | CSV/spreadsheet import validation | Build | C | File/row band |
| 47 | data.deduplicate | Contact/company deduplication suggestions | Hybrid | C | Record/batch band |
| 48 | addresses.normalise | Licensed address normalisation/geocoding | Partner/hybrid | C | Lookup |
| 49 | contacts.email-check | Syntax/domain/deliverability signals | Hybrid | C | Address |
| 50 | contacts.phone-validate | Phone format validation, no ownership assurance | Build | C | Request or included |
| 51 | messaging.sms-otp | Transactional messages/OTP through delivery partner | Partner | C | Message segment |
| 52 | messaging.email | Transactional email delivery | Partner | C | Message |
| 53 | messaging.whatsapp | Approved business messaging through provider | Partner | D | Provider-defined billable unit |
| 54 | logistics.track | Supported carrier tracking aggregation | Partner | D | Tracking entity/query plan |
| 55 | payments.link | Create payment links through approved partner | Partner | D | Contracted gateway fee model |
| 56 | finance.reconcile | Match customer-supplied payment/invoice records | Hybrid | C | Record/batch band |

Utility supply is competitive; validate margin before productising each one. Sandbox PDF generation must prevent arbitrary network/file access. No bulk unsolicited messaging, inferred contact ownership or guaranteed email delivery. Signature legal assurances depend on actual method and use case.

## AI services

| # | Product slug | Service and buyer value | Delivery | Priority | Billing unit |
| --- | --- | --- | --- | --- | --- |
| 57 | ai.summarise | Grounded document summaries for operations | AI | C | Token/size band |
| 58 | ai.classify-ticket | Email/ticket routing labels for support | AI | C | Item/token band |
| 59 | ai.extract | Schema-based text extraction for software teams | AI | C | Token/size band |
| 60 | ai.contract-clauses | Locate clauses/obligations with evidence | AI | C | Document/token band |
| 61 | ai.cv-parse | Candidate field capture for HR platforms | AI | C | Document/token band |
| 62 | ai.product-categorise | Ecommerce catalogue mapping | AI | C | Item/batch band |
| 63 | ai.translate | Supported language translation for business | AI | C | Character/token band |
| 64 | audio.transcribe | Evaluated speech-to-text for local accents | Specialist AI | C | Audio minute |
| 65 | audio.summarise-call | Summary and actions from a transcript | AI | C | Transcript token/size band |
| 66 | privacy.redact | Sensitive-field detection/redaction assistance | Hybrid | C | Page/token band |
| 67 | search.semantic | Tenant-isolated search over supplied documents | AI | D | Ingestion + storage + query |
| 68 | ai.document-questions | Answers grounded in supplied documents with citations | AI | D | Ingestion + query/token band |

Validate language support on actual local samples. Do not promise complete redaction or legal correctness. Retrieval must filter tenant/permissions before selecting context. No autonomous hiring/credit decisions. General AI is optional; deterministic tools and specialist checks must work independently of a cloud LLM.

## Bundles

- Applicant onboarding: ID extraction, liveness and face comparison; optional approved identity check. Return each result separately.
- Supplier onboarding: company lookup, bank verification and document capture; additional tax/B-BBEE services only with explicit scope.
- Accounts payable: invoice extraction, duplicate detection and arithmetic checks.
- Income document processing: payslip/statement capture and cross-document consistency, not a guaranteed affordability decision.
- Delivery capture: OCR, reference extraction and proof-of-delivery indexing.

Bundle execution needs explicit partial-failure charging, maximum cost, step-level evidence and review outcomes. Product count is not a success metric: paid recurring use, accuracy and sustainable contribution are.
