namespace Seko.Infrastructure.Agent.Skills;

public static class SekoBuiltInSkills
{
    public static IReadOnlyCollection<ISekoSkill> CreateAll()
    {
        return
            new ISekoSkill[]
            {
                new DeclarativeSekoSkill(
                    new SekoSkillDescriptor(
                        "coding",
                        "Coding",
                        "Inspect, modify, build and verify software projects.",
                        new[]
                        {
                            "code",
                            "coding",
                            "implement",
                            "fix",
                            "bug",
                            "refactor",
                            "build",
                            "test",
                            "class",
                            "function",
                            "api"
                        },
                        new[]
                        {
                            "filesystem.read"
                        },
                        new[]
                        {
                            "filesystem.write",
                            "project.build",
                            "source.control.diff"
                        },
                        """
                        Work from real project evidence. Prefer the smallest correct
                        change, preserve existing architecture, verify build-relevant
                        modifications, and avoid inventing files or APIs.
                        """,
                        10)),

                new DeclarativeSekoSkill(
                    new SekoSkillDescriptor(
                        "ui-ux",
                        "UI / UX Design",
                        "Design and implement coherent interfaces across code and design tools.",
                        new[]
                        {
                            "ui",
                            "ux",
                            "design",
                            "layout",
                            "figma",
                            "wireframe",
                            "mockup",
                            "prototype",
                            "interface"
                        },
                        new[]
                        {
                            "filesystem.read"
                        },
                        new[]
                        {
                            "filesystem.write",
                            "design.inspect",
                            "design.edit",
                            "asset.export"
                        },
                        """
                        Inspect the existing visual language before changing it.
                        Preserve hierarchy, spacing, typography and interaction
                        consistency. Prefer reusable components and verify that design
                        work maps cleanly to the target product implementation.
                        """,
                        12)),

                new DeclarativeSekoSkill(
                    new SekoSkillDescriptor(
                        "game-development",
                        "Game Development",
                        "Coordinate code, content and engine workflows for game projects.",
                        new[]
                        {
                            "game",
                            "unity",
                            "unreal",
                            "godot",
                            "scene",
                            "prefab",
                            "shader",
                            "player",
                            "level"
                        },
                        new[]
                        {
                            "filesystem.read"
                        },
                        new[]
                        {
                            "filesystem.write",
                            "project.build",
                            "game.editor",
                            "3d.edit"
                        },
                        """
                        Respect engine/project conventions, keep generated content
                        deterministic where possible, and verify changes through the
                        engine/build pipeline rather than only editing source files.
                        """,
                        12)),

                new DeclarativeSekoSkill(
                    new SekoSkillDescriptor(
                        "research",
                        "Research",
                        "Gather, compare and synthesize evidence with traceable sources.",
                        new[]
                        {
                            "research",
                            "compare",
                            "source",
                            "citation",
                            "paper",
                            "thesis",
                            "literature",
                            "evidence"
                        },
                        Array.Empty<string>(),
                        new[]
                        {
                            "web.search",
                            "document.read",
                            "citation.manage"
                        },
                        """
                        Separate evidence from inference, preserve source provenance,
                        compare competing claims, and surface uncertainty instead of
                        filling gaps with assumptions.
                        """,
                        8))
            };
    }
}
