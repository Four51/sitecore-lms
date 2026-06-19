export interface DoceboUserSearchResponse {
    data: DoceboUserSearchData;
    version: string;
    _links: any[];
}

export interface DoceboUserExistsResponse {
    exists: boolean;
}

export interface DoceboUserSearchData {
    items: DoceboUserSearchItem[];
    count: number;
    has_more_data: boolean;
    cursor: any;
    current_page: number;
    current_page_size: number;
    total_page_count: number;
    total_count: number;
    sort: DoceboUserSearchSort[];
}

export interface DoceboUserSearchItem {
    user_id: string;
}

export interface DoceboUserSearchSort {
    sort_attr: string;
    sort_dir: string;
}