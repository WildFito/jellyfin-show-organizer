\# Release Versioning Rule



ShowOrganizer uses four-part versions:



MAJOR.MINOR.PATCH.REVISION



Example:

0.1.2.0



\## Mandatory version rule



Before building, packaging, validating, committing, or preparing a release:



1\. Determine the latest version that has already been released/tagged.

2\. Determine the version currently declared by the repository.

3\. If the current task introduces changes that will be released and the

&#x20;  repository version is equal to an already-released version, increment

&#x20;  the version BEFORE running the release dry-run.



Never package new release code using a version that has already been released.



Example:



Latest released version:

0.1.1.0



New code changes have been made.



Incorrect:

showorganizer\_0.1.1.0.zip



Correct:

showorganizer\_0.1.2.0.zip



\## Important



Do NOT increment the version for every commit.



If the repository has already been bumped to an unreleased version, such as

0.1.2.0, additional work intended for that same upcoming release remains

0.1.2.0.



Only another release after 0.1.2.0 has actually been released requires the

next increment.



\## Release validation



Before declaring a release dry-run successful, verify that all of these

agree:



\- build.yaml version

\- DLL FileVersion

\- generated meta.json

\- package filename

\- release/tag version



The candidate version MUST be newer than the latest existing release/tag.



If it is not newer, STOP the release process and report the versioning error.



\## Tagging



Never manually create a Git tag or GitHub Release.



Use the repository's validated Release ShowOrganizer workflow after:

\- build succeeds

\- all tests pass

\- package validation passes

\- version validation passes

\- user approves the release

