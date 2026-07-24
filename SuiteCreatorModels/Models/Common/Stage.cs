namespace SuiteCreatorAvalonia.Models.Common
{
    public abstract partial class Stage
    {
        public static readonly Guid StartStageId = new Guid("6717334d-289b-46ba-be3c-3b5f11e33556");
        public static readonly Guid EndStageId = new Guid("a891e4ae-7e9a-4f56-bb59-1675f0c64cb2");

        public Guid Id { get; set; } = Guid.NewGuid();
        public string? Name { get; set; }

        public bool IsStart => Id == StartStageId;
        public bool IsEnd => Id == EndStageId;

        public abstract Stage Clone();

        public virtual void UpdateFrom(Stage stage)
        {
            if (stage != null)
            {
                Id = stage.Id;
                Name = stage.Name;
            }
        }
    }

    public class DefaultStage : Stage
    {
        public string? Description { get; set; }

        public override DefaultStage Clone()
        {
            return new DefaultStage
            {
                Id = Id,
                Name = Name,
                Description = Description,
            };
        }

        public override void UpdateFrom(Stage defaultStage)
        {
            if (defaultStage is DefaultStage ds)
            {
                Id = ds.Id;
                Name = defaultStage.Name;
                Description = ds.Description;
            }
        }
    }
}