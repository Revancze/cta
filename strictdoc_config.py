from strictdoc.core.project_config import ProjectConfig


def create_config() -> ProjectConfig:
    return ProjectConfig(
        project_title="CTA",
        project_features=[
            "TABLE_SCREEN",
            "TRACEABILITY_SCREEN",
            "DEEP_TRACEABILITY_SCREEN",
            "SEARCH",
            "TRACEABILITY_MATRIX_SCREEN",
            "REQUIREMENT_TO_SOURCE_TRACEABILITY",
        ],
        include_doc_paths=[
            "/docs/requirements/**",
        ],
        include_source_paths=[
            "/tests/**",
        ],
        exclude_source_paths=[
            "/tests/**/bin/**",
            "/tests/**/obj/**",
        ],
    )
