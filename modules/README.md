<!-- DO NOT EDIT: This file is programmatically generated and managed by an external tool. It is a standard file that is copied and maintained in every repository. Any changes will be overwritten. -->
<!-- markdownlint-disable MD033 -->

# Modules

<table style="width: 100%; border-style: none;">
<tr>
    <td style="width: 80px; text-align: center;">
        <a href="https://aureliasrs.ca/">
            <img
                width="64px"
                src="./.assets/img/icon.svg"
                alt="modules icon"/>
        </a>
    </td>
    <td>
        <strong>Infrastructure Modules</strong><br />
        Reusable infrastructure building blocks for deployable roots<br />
    </td>
</tr>
</table>

---

The `modules` directory contains reusable infrastructure modules local to this repository.

Modules define composable implementation details. They are consumed by deployable roots in directories such as `env` and `workspaces`, which provide concrete deployment wiring and configuration.

## Design

Modules should be small, focused, and reusable across deployment targets in the same repository.

They should avoid environment-specific assumptions unless those assumptions are exposed as explicit inputs. A module may define infrastructure behaviour, but it should not represent a concrete deployment by itself unless it is intentionally designed to also operate as a standalone root.

## Rules

- Keep modules reusable across environments and workspaces.
- Keep provider requirements explicit.
- Prefer clear inputs and outputs over implicit environment assumptions.
- Avoid embedding deployment-specific names, tiers, or account details directly in modules.
- Do not commit module-local state files, provider plugin directories, or local cache output.
- Avoid module lock files unless the module is also a standalone deployable root.
