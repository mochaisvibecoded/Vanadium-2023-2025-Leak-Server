namespace Vanadium.enums
{
    // AEBMLPPCPJI
    public enum InventionPermissions
    {
    	Unassigned,
		LimitedOneUseOnly = 10,
		DisallowKeyLock = 15,
		UseOnly = 20,
		EditAndSave = 40,
		Publish = 60,
		Charge = 80,
		Unlimited = 100
    }
}