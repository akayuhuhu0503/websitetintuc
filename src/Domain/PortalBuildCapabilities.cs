namespace LandPortal.Domain;
public static class PortalBuildCapabilities
{
#if LANDPORTAL_LAN_SCHEMA15
    public static bool LegacyLanSchema15 => true;
#else
    public static bool LegacyLanSchema15 => false;
#endif
}
