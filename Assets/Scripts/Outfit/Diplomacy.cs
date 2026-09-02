namespace LivingCity.Outfit
{
    /// <summary>
    /// The three-step scale. What each stance DOES (once execution lands - the state
    /// and its wording ship now so the player is never surprised later):
    ///
    ///  Peace - no engagement. Your men and theirs pass in the street, claimed ground
    ///          or not.
    ///  Truce - territorial. Their men engage yours caught inside THEIR territory, and
    ///          yours engage theirs on YOURS. Neutral ground stays quiet.
    ///  War   - on sight. Their men engage yours anywhere in the city, and yours theirs.
    /// </summary>
    public enum Stance
    {
        Peace,
        Truce,
        War,
    }

}
