using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace UEVR;
public static class CleanupExclusions
    {  
    //these would only be present if the backend successfully initialized at least once
    static readonly string [ ] uevrFiles = {
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
        var count = 0;
        try{
            if ( !Directory.Exists ( path ) )
                return 0;
            else count = Directory.GetFiles ( path ).Length;
            }
        catch (Exception ) { }
         return count;
        }

    static bool ValidateLog ( string path )
        {
        if ( !File.Exists ( path ) ) return false;
        foreach ( var line in File.ReadLines ( path ) )
            {
            if ( line.EndsWith ( "Framework initialized" ) )
                return true;
            }
        return false;
        }

    public static void Cleanup ( )
        {
        try {
            string unrealvrmod = Path.Combine ( Environment.GetFolderPath ( Environment.SpecialFolder.ApplicationData ), "UnrealVRMod" );

            if ( Directory.GetCurrentDirectory ( ) != unrealvrmod )
                {
                Directory.SetCurrentDirectory ( unrealvrmod );
                }

            string uevr = Path.Combine ( unrealvrmod, "UEVR" );
            string uevrNightly = Path.Combine ( unrealvrmod, "uevr-nightly" );
            List<string> profiles = Directory.GetDirectories ( unrealvrmod ).Where ( Directory.Exists ).ToList ( );
            //Sort to bottom of list for ease
            string exclude = Path.Combine ( unrealvrmod, "zExcluded" );
            if (!Directory.Exists(exclude))           
                Directory.CreateDirectory ( exclude );
            profiles.Remove ( uevr );
            profiles.Remove ( uevrNightly );
            profiles.Remove ( exclude );

            var profilesToRemove = new List<string> ( );

            foreach ( string prof in profiles )
                {
                if ( prof.ToLower ( ).EndsWith ( "win64-shipping" ) ) continue;
                if ( prof.ToLower ( ).Contains ( "uevr" ) ) continue;
                //if any of these cases are true then the mod has loaded in at least once
                if ( prof == uevr || prof == uevrNightly || FileCount ( Path.Combine ( prof, "plugins" ) ) >= 1 || FileCount ( Path.Combine ( prof, "scripts" ) ) >= 1 || FileCount ( Path.Combine ( prof, "uobjecthook" ) ) >= 1 || FileCount ( Path.Combine ( prof, "sdkdump" ) ) >= 1 || uevrFiles.Any ( f => File.Exists ( Path.Combine ( prof, f ) ) ) ||  ValidateLog ( Path.Combine ( prof, "log.txt" ) ) )
                    {
                    continue;
                    }
                profilesToRemove.Add ( prof );
                }

            foreach ( string prof in profilesToRemove )
                {
                profiles.Remove ( prof );
                var excludedProf = Path.Combine ( exclude, Path.GetRelativePath ( unrealvrmod, prof ) );
                if ( !Directory.Exists ( excludedProf ) )
                    Directory.CreateDirectory ( excludedProf );
                GameConfig.MoveDirectoryContents ( prof, excludedProf );              
                }

            //var include = Path.Combine (exclude, "include.txt" );
            //File.WriteAllLines ( include, profiles );
            }
        catch(Exception ) { }
        }

    }
