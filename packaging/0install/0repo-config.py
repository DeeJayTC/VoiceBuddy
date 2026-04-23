# 0repo configuration for the VoiceBuddy feed.
# Consumed by the `0repo` tool. See https://docs.0install.net/tools/0repo/
#
# State is kept under packaging/0install/repo/ (created by `0repo create`).
# CI copies the generated public feed into docs/ so GitHub Pages serves it.

REPOSITORY_BASE_URL = "https://deejaytc.github.io/VoiceBuddy/"

# We don't host archives on the Pages site — they live as GitHub Release assets.
# This pattern tells 0repo to leave archive URLs untouched rather than trying to
# rewrite them to a REPOSITORY_BASE_URL-relative path.
ARCHIVES_BASE_URL = "https://github.com/DeeJayTC/VoiceBuddy/releases/download/"

LOCAL_ARCHIVES_BACKUP_DIR = None  # No local mirror; GH Releases is authoritative.

# 0repo verifies the SHA-1/SHA-256 manifest digests on the referenced archive
# before accepting a new implementation. Requires the archive URL to be
# reachable from the CI runner (it is — it's a public GitHub Release asset).
CHECK_DIGESTS = True

# Contributor list is used when 0repo runs in multi-uploader mode. For our
# single-maintainer CI-driven setup the GPG key identity below is the only
# authority that matters.
CONTRIBUTORS = {}

# Signing key fingerprint. Populated from the GPG_KEY_FP environment variable
# at CI time so the fingerprint never needs to be committed.
import os
GPG_SIGNING_KEY = os.environ.get("GPG_KEY_FP", "")


def upload(files):
    """Called by `0repo` after the public/ directory has been updated.

    We don't push anywhere from inside 0repo — the surrounding GitHub Actions
    job commits the updated docs/voice-buddy.xml back to the repo. Leaving this
    as a no-op keeps 0repo from trying to rsync/ssh from CI.
    """
    return
