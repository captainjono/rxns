namespace Rxns.DDD
{
    public class RxnsRole
    {
        public bool IsExternal { get; private set; }
        public string Description { get; set; }
        public string RoleName { get; set; }
        public string RoleId { get; set; }

        public RxnsRole()
        {

        }

        public RxnsRole(string description, bool isExternal = false)

        {
            Update(description, isExternal);
        }

        public void Update(string name, bool isExternal)
        {
            Description = name;
            IsExternal = isExternal;
        }
    }
}
