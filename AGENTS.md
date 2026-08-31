# Release security review

Before creating or publishing a release, perform a security review of every changed, untracked, and packaged file.

At minimum, inspect the Git diff and untracked files; scan for secrets, tokens, `auth.json`, `profiles.json`, personal data, and sensitive logs; review risky filesystem, process, and network operations; and verify installer scripts, release workflows, and artifact contents.

Do not create a release tag, publish a GitHub Release, upload release assets, or install the release locally while a security issue or sensitive file remains unresolved. Report the review result and any accepted residual risk before deployment.
