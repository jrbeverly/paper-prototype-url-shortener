<!-- DO NOT EDIT: This file is programmatically generated and managed by an external tool. It is a standard file that is copied and maintained in every repository. Any changes will be overwritten. -->
<!-- markdownlint-disable MD033 -->

# Environments

<table style="width: 100%; border-style: none;">
<tr>
    <td style="width: 80px; text-align: center;">
        <a href="https://aureliasrs.ca/">
            <img
                width="64px"
                src="./.assets/img/icon.svg"
                alt="actions logo"/>
        </a>
    </td>
    <td>
        <strong>Infrastructure Environments</strong><br />
        Deployable infrastructure roots for services, sites, and supporting systems<br />
    </td>
</tr>
</table>

---

The `env` directory contains the deployable infrastructure definitions for this repository.

Each environment root represents a concrete deployment target used by continuous deployment, such as a production deployment, non-production deployment, variant deployment, or pull request deployment.

This directory describes what can be deployed. It does not contain every reusable infrastructure implementation detail.

## Design

Environment roots are organized around logical deployed units, such as a website, service, secrets system, or supporting infrastructure area.

They should be thin, readable entry points that compose reusable modules and provide the configuration needed for a specific deployment target.

Terraform is the primary format used here, but this directory is not limited to Terraform. Other deployment definitions may also live here when they represent deployable roots that can be executed by the repository’s deployment orchestration system.

## Deployment expectations

Each deployable root should be executable by a generic deployment system using standard repository conventions.

Deployments should not require ad hoc parameters at execution time. Required values should come from committed configuration, directory conventions, module defaults, data sources, or other well-defined sources that the deployer can resolve consistently.

## Relationship to modules and shared infrastructure

Reusable infrastructure belongs in modules, either local to this repository or provided externally.

The `env` directory should compose those modules for real deployment targets. Shared infrastructure, such as VPCs, DNS zones, or platform-level services, may be referenced from environment roots but does not need to be owned here unless this repository is responsible for deploying it.

Use deployment configurations should rely on `workspaces/`

## Rules

- Keep reusable infrastructure in modules.
- Keep deployable environment wiring in environment roots.
- Keep roots thin, explicit, and readable.
- Organize roots by logical deployed unit and environment tier or variant.
- Commit provider lock files for deployable Terraform roots.
- Do not commit state files, local variable files, provider plugin directories, or local cache output.
- Do not require deployment-specific parameters to be passed manually at execution time.
