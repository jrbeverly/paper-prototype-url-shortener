<!-- DO NOT EDIT: This file is programmatically generated and managed by an external tool. It is a standard file that is copied and maintained in every repository. Any changes will be overwritten. -->
<!-- markdownlint-disable MD033 -->

# Scripts

<table style="width: 100%; border-style: none;">
<tr>
    <td style="width: 80px; text-align: center;">
        <a href="https://aureliasrs.ca/">
            <img
                width="64px"
                src="./.assets/img/icon.svg"
                alt="scripts icon"/>
        </a>
    </td>
    <td>
        <strong>Repository Scripts</strong><br />
        Deterministic command helpers for make modules, CI workflows, and local tooling<br />
    </td>
</tr>
</table>

---

## Purpose

The `scripts` directory contains repository scripts used by modular makefiles, CI workflows, and local developer tooling.

Scripts are for behaviour that is too large or awkward to keep directly inside a make recipe. They provide clear command entry points while leaving orchestration decisions to the caller.

## Design

Scripts should be narrow, deterministic, and explicit about their inputs and outputs.

They should take component lists, paths, modes, and other execution details from arguments or environment variables provided by the caller. They should not rely on hidden discovery, ambient repository state, or implicit setup steps unless those behaviours are part of the documented contract.

## Rules

- Prefer standard input, arguments, environment variables, and exit codes over hidden state.
- Take component lists from explicit arguments or environment variables.
- Exit nonzero when a required check fails.
- Print clear `skip:` messages when a capability is intentionally unavailable.
- Keep generated output paths explicit.
- Keep dependency installation and long-running setup out of ad hoc scripts.
- Keep networked setup separate from offline validation.
