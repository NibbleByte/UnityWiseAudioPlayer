============= Unity Asset Store Packaging Complications =============

This package is using UPM format with hidden samples in the "Samples~" folder. They are described in the package.json.
Uploading the package as .unitypackage to the asset store won't support "Samples" button in the Package Manager.
Unity added special UNITY_ASTOOLS_EXPERIMENTAL define that allows uploading .unitypackage from the Packages folder as "Custom Package".
When user imports the package it will be placed again in the Packages folder and the "Samples" button will work.

We use this "hybrid" approach as we can't enroll for official Unity UPM program (we don't have website domain to use).

More info:
* https://docs.unity3d.com/6000.6/Documentation/Manual/upm-package-types.html
* https://discussions.unity.com/t/on-asset-store-new-workflow/933106
* https://discussions.unity.com/t/package-manager-wont-import-sample-when-using