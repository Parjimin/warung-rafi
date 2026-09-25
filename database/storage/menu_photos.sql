-- Supabase SQL Editor only. Run after public migrations, on the project's Storage schema.
-- Photos are public menu assets. No anon/authenticated write policy is granted.
insert into storage.buckets(id,name,public,file_size_limit,allowed_mime_types)
values('menu-photos','menu-photos',true,3000000,array['image/jpeg'])
on conflict(id) do update set public=true,file_size_limit=3000000,allowed_mime_types=array['image/jpeg'];
