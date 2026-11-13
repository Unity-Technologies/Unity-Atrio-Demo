using UnityEngine.Scripting;

// Nothing references this assembly directly: InputPromptView registers itself as the default prompt look
// at startup. Without this, managed code stripping drops the assembly from player builds and prompts never draw.
[assembly: AlwaysLinkAssembly]
