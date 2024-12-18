using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UEVR;
public static class CleanupExclusions
    {
    static readonly string [ ] knownFiles = {
            "bindings_vive_controller.json",
            "cameras.txt",
            "actions.json",
            "binding_rift.json",
            "binding_vive.json",
            "bindings_knuckles.json",
            "bindings_oculus_touch.json"
        };

    static int FileCount ( string path )
        {
        if ( !Directory.Exists ( path ) )
            return 0;
        return Directory.GetFiles ( path ).Length;
        }

    static bool ValidateLog ( string path )
        {
        foreach ( var line in File.ReadLines ( path ) )
            {
            if ( line.EndsWith ( "Framework initialized" ) )
                return true;
            }
        return false;
        }

    public  static void Cleanup ( )
        {
        string unrealvrmod = Path.Combine ( Environment.GetFolderPath ( Environment.SpecialFolder.ApplicationData ), "UnrealVRMod" );

        if ( Directory.GetCurrentDirectory ( ) != unrealvrmod )
            {
            Directory.SetCurrentDirectory ( unrealvrmod );
            }

        string uevr = Path.Combine ( unrealvrmod, "UEVR" );
        string uevrNightly = Path.Combine ( unrealvrmod, "uevr-nightly" );
        List<string> profiles = Directory.GetDirectories ( unrealvrmod ).Where ( Directory.Exists ).ToList ( );
        string exclude = Path.Combine ( unrealvrmod, "excluded" );
        Directory.CreateDirectory ( exclude );
        profiles.Remove ( uevr );
        profiles.Remove ( uevrNightly );
        profiles.Remove ( exclude );

        foreach ( string prof in profiles )
            {
            if ( prof.ToLower ( ).EndsWith ( "win64-shipping" ) ) continue;
            if ( prof.ToLower ( ).Contains ( "uevr" ) ) continue;
            if ( prof == uevr ) continue;
            if ( prof == uevrNightly ) continue;
            if ( FileCount ( Path.Combine ( prof, "plugins" ) ) >= 1 ) continue;
            if ( FileCount ( Path.Combine ( prof, "scripts" ) ) >= 1 ) continue;
            if ( FileCount ( Path.Combine ( prof, "uobjecthook" ) ) >= 1 ) continue;
            if ( FileCount ( Path.Combine ( prof, "sdkdump" ) ) >= 1 ) continue;
            if ( knownFiles.Any ( f => File.Exists ( Path.Combine ( prof, f ) ) ) ) continue;
            if ( File.Exists ( Path.Combine ( prof, "log.txt" ) ) && ValidateLog ( Path.Combine ( prof, "log.txt" ) ) ) continue;
            Directory.Move ( prof, Path.Combine ( exclude, Path.GetRelativePath ( unrealvrmod, prof ) ) );
            }



        }
    }
