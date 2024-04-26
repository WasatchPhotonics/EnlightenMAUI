# Overview

EnlightenMAUI is a port of the earlier Xamarin-based EnlightenMobile. Microsoft 
is killing support for Xamarin, so the port is non-optional (yet largely 
painless).

# Dependencies

- Visual Studio 2022 Community Edition
- Telerik UI for MAUI (free demo works)

# Porting Notes

I tried simply doing an in-place port within the old repo, starting from Samie's
MAUI branch, but I could never get it to flash or run. Ended up just making this
new repo and rebuilding the app step-by-step within the new framework. Goal is to
basically copy over the guts of each class one-by-one in priority order.

For expediency, kept Telerik, moving to their new MAUI product.

# Roadmap

See [docs/BACKLOG.md](BACKLOG).

# Troubleshooting

## OpenGL

    [libEGL] call to OpenGL ES API with no current context (logged once per thread)
    **System.Reflection.TargetInvocationException:** 'Exception has been thrown by the target of an invocation.'

[Register Telerik handles](https://docs.telerik.com/devtools/maui/get-started/windows/first-steps-nuget#step-5-register-the-required-handlers).

## appicon 

Periodically Visual Studio forgets how to build the solution and complains about
missing appicon and appiconfg files in AndroidManifest.xml, even though they're
plainly there. Current solution:

- close Visual Studio
- 'make clean'
- re-launch Visual Studio
- delete Resources/appicon and appiconfg
- re-add appicon and appconfg (copy from EnlightenMobile's MAUI branch)
- set both to "Build Action: MauiIcon"
- rebuild

# Changelog

See [CHANGELOG.md](CHANGELOG).
# References

- ...
