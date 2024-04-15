////////////////////////////////////////////////////////////////////////////////
// Imported from EnlightenMobile, but not ported to MAUI or tested; does not 
// seem to be needed?
////////////////////////////////////////////////////////////////////////////////

/*
using System;
using System.Threading.Tasks;
using Xamarin.Essentials;

namespace EnlightenMAUI.Models;

/// <remarks>
/// "Location" and "Geolocation" would cause confusion with framework classes
/// </remarks>
///
/// <todo>
/// Maybe kick-off a call to update() every acquire, or on a timed interval?  
/// Not core functionality of this app.
/// </todo>
public class WhereAmI
{
    static WhereAmI instance;
    static object mut = new object();

    public Location location { get; private set; }

    Logger logger = Logger.getInstance();

    static public WhereAmI getInstance()
    {
        lock(mut)
        {
            if (instance is null)
                instance = new WhereAmI();
        }
        return instance;
    }

    WhereAmI()
    {
        // kick it off in the background, don't worry about when it completes
        Task.Run(() => initAsync());
    }

    async Task<bool> initAsync()
    {
        try
        {
            var request = new GeolocationRequest(GeolocationAccuracy.Medium);
            location = await Geolocation.GetLocationAsync(request);

            if (location != null)
            {
                dump("init");
                return true;
            }
            else
            {
                logger.error($"WhereAmI: unable to determine location");
            }
        }
        catch (FeatureNotSupportedException fnsEx)
        {
            logger.error($"WhereAmI: not supported on device: {fnsEx}");
        }
        catch (FeatureNotEnabledException fneEx)
        {
            logger.error($"WhereAmI: not enabled on device: {fneEx}");
        }
        catch (PermissionException pEx)
        {
            logger.error($"WhereAmI: not permitted on device: {pEx}");
        }
        catch (Exception ex)
        {
            logger.error($"WhereAmI: caught exception: {ex}");
        }
        return false;
    }

    async public Task<bool> updateAsync()
    {
        try
        {
            var newLoc = await Geolocation.GetLastKnownLocationAsync();

            if (newLoc != null)
            {
                location = newLoc;
                dump("update");
                return true;
            }
        }
        catch(Exception ex)
        {
            logger.error($"WhereAmI: exception updating location: {ex}");
        }
        return false;
    }

    void dump(string label)
    {
        logger.info($"WhereAmI({label}): lat {location.Latitude}, lon {location.Longitude}, alt {location.Altitude}");
    }
}
*/
