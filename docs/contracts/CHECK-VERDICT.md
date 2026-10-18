# Pointer - CHECK-VERDICT

- **Id/version read:** CHECK-VERDICT 0.13, draft, 2026-10-18.
- **Home:** shared catalog, `automated-checks/README.md`, sections 2, 10 and consolidation.
- **Role:** producer (checks, TRX wrapper), consumer (build/release orchestration).
- **Implementation:** partial; [audit](../design/contract-sync-20261017.md), E3.

`tools/checks/CheckVerdict.psm1` writes canonical words and retains the exit-code
precedence: failure, could-not-verify, advisory, pass, deliberate not-applicable.
Counted test verdicts use `run=N skipped=M`. Empty requested runs and skipped
requested tests are could-not-verify, never a clean pass. The TRX wrapper reads
executed/failed/notExecuted counters; missing or malformed input is could-not-verify.
Build/preflight now honor the wrapper's nonzero result.

`tools/checks/Test-ContractSync.ps1` exercises subprocess exits/final lines,
zero/skip/fail precedence and synthetic TRX counters. Outer build/release
exceptional paths and complete child-verdict aggregation remain E3; helper tests
do not imply that every caller satisfies every path. Prior adoption versions
remain unchanged until that evidence is complete.
