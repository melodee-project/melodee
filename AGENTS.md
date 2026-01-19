## Melodee AI Agent Guide

### Overview
This document serves as a guide for AI agents interacting with the Melodee solution. It outlines the project structure, coding standards, and available resources to ensure consistent and high-quality contributions.

### Project Context
Melodee is a comprehensive music and media management system built with .NET (C#) and Blazor. It includes features like Party Mode, Jukebox functionality, and media library management.

### AI Resources
This repository contains a suite of configuration files designed to guide AI behavior, located in the [`.github/`](./.github/) directory.

### Quick start: pick the right instruction set
Before editing, open the most relevant instruction file(s) under [`.github/instructions/`](./.github/instructions/).

- **Security-sensitive changes** (auth, tokens, cookies, file paths, external URLs, anything user-supplied): [`security-and-owasp.instructions.md`](./.github/instructions/security-and-owasp.instructions.md)
- **Performance-sensitive changes** (hot paths, DB queries, streaming, large collections): [`performance-optimization.instructions.md`](./.github/instructions/performance-optimization.instructions.md)
- **Code review output** (reviewing PRs/patches): [`code-review-generic.instructions.md`](./.github/instructions/code-review-generic.instructions.md)
- **Docs** (any `*.md`): [`markdown.instructions.md`](./.github/instructions/markdown.instructions.md)
- **Playwright tests**:
  - [.NET](./.github/instructions/playwright-dotnet.instructions.md)
  - [TypeScript](./.github/instructions/playwright-typescript.instructions.md)
  - [Python](./.github/instructions/playwright-python.instructions.md)

### 1. Custom Instructions (`.github/instructions/`)
These files define the specific coding standards, architectural guidelines, and best practices for the project. Agents **MUST** adhere to these instructions when working on relevant parts of the codebase.

See: [`.github/instructions/`](./.github/instructions/)

- **ASP.NET & Blazor**:
  - [`aspnet-rest-apis.instructions.md`](./.github/instructions/aspnet-rest-apis.instructions.md)
  - [`blazor.instructions.md`](./.github/instructions/blazor.instructions.md)
  - [`blazor-localization.instructions.md`](./.github/instructions/blazor-localization.instructions.md)
- **Languages**:
  - [`csharp.instructions.md`](./.github/instructions/csharp.instructions.md)
  - [`python.instructions.md`](./.github/instructions/python.instructions.md)
  - [`shell.instructions.md`](./.github/instructions/shell.instructions.md)
  - [`yaml.instructions.md`](./.github/instructions/yaml.instructions.md)
  - [`markdown.instructions.md`](./.github/instructions/markdown.instructions.md)
- **Data & ORM**:
  - [`ef-core-migrations.instructions.md`](./.github/instructions/ef-core-migrations.instructions.md)
  - [`nodatime.instructions.md`](./.github/instructions/nodatime.instructions.md)
- **Quality & Testing**:
  - [`testing.instructions.md`](./.github/instructions/testing.instructions.md)
  - [`playwright-dotnet.instructions.md`](./.github/instructions/playwright-dotnet.instructions.md)
  - [`playwright-typescript.instructions.md`](./.github/instructions/playwright-typescript.instructions.md)
  - [`playwright-python.instructions.md`](./.github/instructions/playwright-python.instructions.md)
  - [`code-review-generic.instructions.md`](./.github/instructions/code-review-generic.instructions.md)
- **Architecture & Best Practices**:
  - [`dependency-injection.instructions.md`](./.github/instructions/dependency-injection.instructions.md)
  - [`dotnet-architecture-good-practices.instructions.md`](./.github/instructions/dotnet-architecture-good-practices.instructions.md)
  - [`performance-optimization.instructions.md`](./.github/instructions/performance-optimization.instructions.md)
  - [`security-and-owasp.instructions.md`](./.github/instructions/security-and-owasp.instructions.md)
  - [`self-explanatory-code-commenting.instructions.md`](./.github/instructions/self-explanatory-code-commenting.instructions.md)
  - [`task-implementation.instructions.md`](./.github/instructions/task-implementation.instructions.md)
  - [`github-actions-ci-cd-best-practices.instructions.md`](./.github/instructions/github-actions-ci-cd-best-practices.instructions.md)
- **Infrastructure**:
  - [`docker.instructions.md`](./.github/instructions/docker.instructions.md)

### 2. Agent Personas (`.github/agents/`)
Specialized agent definitions for specific tasks. Use these personas to adopt the appropriate mindset and toolset.

See: [`.github/agents/`](./.github/agents/)

- [`expert-dotnet-software-engineer.agent.md`](./.github/agents/expert-dotnet-software-engineer.agent.md): General purpose high-level .NET engineering.
- [`csharp-dotnet-janitor.agent.md`](./.github/agents/csharp-dotnet-janitor.agent.md): Cleanup and maintenance.
- [`debug.agent.md`](./.github/agents/debug.agent.md): Dedicated debugging specialist.
- [`dotnet-upgrade.agent.md`](./.github/agents/dotnet-upgrade.agent.md): For handling framework upgrades.
- [`tdd-red.agent.md`](./.github/agents/tdd-red.agent.md): TDD Phase 1 - Write failing test.
- [`tdd-green.agent.md`](./.github/agents/tdd-green.agent.md): TDD Phase 2 - Make it pass.
- [`tdd-refactor.agent.md`](./.github/agents/tdd-refactor.agent.md): TDD Phase 3 - Clean up.
- [`se-security-reviewer.agent.md`](./.github/agents/se-security-reviewer.agent.md): Security implementation and review.
- [`playwright-tester.agent.md`](./.github/agents/playwright-tester.agent.md): End-to-end testing with Playwright.
- [`blazor-dotnet10-developer.skill.md`](./.github/agents/blazor-dotnet10-developer.skill.md): Skill for generating Melodee Blazor Server (.NET 10/C# 13) UI code.

### 3. Prompt Library (`.github/prompts/`)
Reusable prompts for common tasks to ensure consistency.

See: [`.github/prompts/`](./.github/prompts/)

- **Code Generation**:
  - [`create-readme.prompt.md`](./.github/prompts/create-readme.prompt.md)
  - [`csharp-async.prompt.md`](./.github/prompts/csharp-async.prompt.md)
  - [`csharp-docs.prompt.md`](./.github/prompts/csharp-docs.prompt.md)
  - [`csharp-xunit.prompt.md`](./.github/prompts/csharp-xunit.prompt.md)
  - [`ef-core.prompt.md`](./.github/prompts/ef-core.prompt.md)
  - [`java-junit.prompt.md`](./.github/prompts/java-junit.prompt.md)
- **Reviews**:
  - [`ai-prompt-engineering-safety-review.prompt.md`](./.github/prompts/ai-prompt-engineering-safety-review.prompt.md)
  - [`conventional-commit.prompt.md`](./.github/prompts/conventional-commit.prompt.md)
  - [`dotnet-best-practices.prompt.md`](./.github/prompts/dotnet-best-practices.prompt.md)
  - [`dotnet-design-pattern-review.prompt.md`](./.github/prompts/dotnet-design-pattern-review.prompt.md)
- **Playwright**:
  - [`playwright-explore-website.prompt.md`](./.github/prompts/playwright-explore-website.prompt.md)
  - [`playwright-generate-test.prompt.md`](./.github/prompts/playwright-generate-test.prompt.md)

### 4. CI/CD Workflows (`.github/workflows/`)
GitHub Actions workflows defining the build and test pipelines.

See: [`.github/workflows/`](./.github/workflows/)

- [`dotnet.yml`](./.github/workflows/dotnet.yml): Main .NET build and test pipeline.
- [`codeql.yml`](./.github/workflows/codeql.yml): Security scanning.
- [`localization.yml`](./.github/workflows/localization.yml): Localization checks.

### Usage Guidelines for Agents
1. **Context awareness**: Before generating code, always check [`.github/instructions/`](./.github/instructions/) for relevant guidelines. For example, if editing a Blazor component, consult [`blazor.instructions.md`](./.github/instructions/blazor.instructions.md).
2. **Persona adoption**: When assigned a specific task type (e.g., "Refactor this"), check if a matching agent persona exists in [`.github/agents/`](./.github/agents/) (e.g., [`tdd-refactor.agent.md`](./.github/agents/tdd-refactor.agent.md)) and align your behavior with it.
3. **Prompt utilization**: If asked to write documentation or tests, check [`.github/prompts/`](./.github/prompts/) to see if a prompt exists to standardize output format (e.g. [`csharp-docs.prompt.md`](./.github/prompts/csharp-docs.prompt.md)).

### Before you change code (fast checklist)
- Read and follow applicable instruction files under [`.github/instructions/`](./.github/instructions/).
- Prefer self-explanatory code; comment only to explain *why* (see [`self-explanatory-code-commenting.instructions.md`](./.github/instructions/self-explanatory-code-commenting.instructions.md)).
- Default to secure patterns (see [`security-and-owasp.instructions.md`](./.github/instructions/security-and-owasp.instructions.md)), especially around input handling and secrets.
- Avoid obvious performance regressions (see [`performance-optimization.instructions.md`](./.github/instructions/performance-optimization.instructions.md)).
- Add/update tests for behavior changes, and run the smallest relevant test suite.

### Documentation Guidelines

**IMPORTANT**: The `/docs` directory is reserved for the Jekyll-based public documentation site (GitHub Pages). **DO NOT** place internal documentation, implementation notes, session summaries, or test guides in `/docs`.

#### Where to Place Documentation

| Document Type | Location | Notes |
|---------------|----------|-------|
| **Public user docs** | `/docs/pages/` | Jekyll-formatted, visible at melodee.org |
| **Implementation notes** | `/design/docs/` | Internal technical documentation |
| **Session summaries** | `/design/docs/` | AI session summaries and fix docs |
| **Requirements specs** | `/design/requirements/` | Feature requirements and specifications |
| **Testing guides** | `/design/testing/` | Manual test walkthroughs, coverage reports |
| **Runbooks** | `/design/runbooks/` | Operational debugging guides |
| **ADRs** | `/design/adr/` | Architecture Decision Records |
| **Chart data** | `/design/charts/` | JSON data files for music charts |

#### Public Documentation (Jekyll)

When creating **public-facing documentation** (user guides, API docs):
1. Create files in `/docs/pages/`
2. Use Jekyll front matter format
3. Update `/docs/_data/toc.yml` for navigation

Example front matter:
```yaml
---
title: Feature Name
description: Brief description of the feature
tags:
  - feature
  - configuration
---
```

#### Internal Documentation

When creating **internal documentation** (implementation notes, debugging guides):
1. Create files in the appropriate `/design/` subdirectory
2. Use standard Markdown (no Jekyll front matter required)
3. Name files descriptively: `feature-name-description.md`

---
*Auto-generated guide for AI collaboration.*
